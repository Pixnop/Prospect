using System.IO.Abstractions;

using Prospect.Core.Common;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Installation Windows : exécution silencieuse de l'installeur Inno Setup officiel.
/// </summary>
/// <remarks>
/// <para>
/// Il n'existe aucun build Windows portable, uniquement cet installeur
/// (docs/research/vslauncher-et-distribution.md, section b), donc pas d'archive à extraire ici :
/// on reprend le jeu d'options éprouvé par VS Launcher, où <c>/CURRENTUSER</c> évite l'élévation
/// UAC et <c>/DIR</c> pose le jeu directement dans le dossier de la version.
/// </para>
/// <para>
/// Ce que ces options ne peuvent PAS faire, et qui a été rapporté en test réel : empêcher une boîte
/// de dialogue posée par le SCRIPT de l'installeur. <c>/SUPPRESSMSGBOXES</c> ne couvre que les
/// messages de Setup lui-même et la fonction <c>SuppressibleMsgBox</c> du langage de script ; un
/// <c>MsgBox</c> nu appelé depuis <c>InitializeSetup</c> — c'est le cas de la question « une
/// ancienne version a été détectée, la désinstaller d'abord ? » — s'affiche quand même. Voir la
/// documentation d'Inno Setup, « Setup Command Line Parameters » et « Pascal Scripting:
/// SuppressibleMsgBox ». Voir cette boîte n'est donc pas la preuve que les arguments ne sont pas
/// arrivés, et c'est pour ça que la vérification qui compte est celle du RÉSULTAT, faite par
/// <see cref="GameInstallService"/> avant d'écrire la sentinelle de complétude.
/// </para>
/// </remarks>
public sealed class WindowsGameInstallStrategy : IGameInstallStrategy
{
    /// <summary>
    /// Options passées à l'installeur, dans l'ordre. Exposées pour que le test vérifie exactement
    /// la ligne de commande : une faute de frappe ici se traduirait par un installeur qui ouvre une
    /// fenêtre en pleine installation silencieuse, ou qui installe ailleurs que prévu.
    /// </summary>
    public static readonly IReadOnlyList<string> SilentArguments =
    [
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/CURRENTUSER",
        "/NOICONS",
    ];

    private readonly IFileSystem _fileSystem;
    private readonly IProcessRunner _processRunner;
    private readonly IAppLog _log;
    private readonly TimeSpan _sampleInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Construit la stratégie.</summary>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    /// <param name="processRunner">Port d'exécution de processus.</param>
    /// <param name="log">
    /// Journal de diagnostic. La ligne de commande exacte y est écrite AVANT le lancement : c'est
    /// la seule pièce qui permette d'arbitrer, depuis un rapport de terrain, entre « les arguments
    /// ne sont pas arrivés » et « l'installeur ne les a pas honorés ».
    /// </param>
    /// <param name="sampleInterval">Période d'échantillonnage du dossier cible. Défaut : une seconde.</param>
    /// <param name="delay">
    /// Attente entre deux échantillons. Paramètre injectable plutôt qu'un appel direct à
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>, même idiome que
    /// <see cref="Prospect.Core.Http.RetryPolicy"/> : <see cref="IClock"/> ne rend que l'heure, pas
    /// un battement, et un test qui attendrait de vraies secondes ne serait pas un test.
    /// </param>
    public WindowsGameInstallStrategy(
        IFileSystem fileSystem,
        IProcessRunner processRunner,
        IAppLog log,
        TimeSpan? sampleInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(log);

        _fileSystem = fileSystem;
        _processRunner = processRunner;
        _log = log;
        _sampleInterval = sampleInterval ?? InstallDirectoryGrowthReporter.DefaultInterval;
        _delay = delay ?? Task.Delay;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> PlatformKeys { get; } = [GamePlatforms.Windows];

    /// <inheritdoc />
    public IReadOnlyList<GameExecutableLocation> ExpectedExecutables { get; } =
    [
        GameExecutableLocation.Of("Vintagestory.exe"),
    ];

    /// <summary>
    /// Argument <c>/DIR</c> pour un dossier cible, sous la forme documentée par Inno Setup.
    /// </summary>
    /// <remarks>
    /// Le séparateur final est retiré, et c'est la seule normalisation qui compte : un
    /// <c>/DIR</c> terminé par un antislash devient, une fois échappé pour Windows, un antislash
    /// DOUBLÉ qui ne veut plus dire la même chose selon le parseur d'en face. Les guillemets, eux,
    /// sont posés par l'échappement d'argv (voir <see cref="ProcessCommandLine"/>) : Inno lit ses
    /// paramètres avec le <c>GetParamStr</c> de Delphi, qui retire les guillemets où qu'ils se
    /// trouvent dans le jeton, donc <c>"/DIR=x:\a b"</c> et <c>/DIR="x:\a b"</c> lui donnent
    /// rigoureusement la même valeur.
    /// </remarks>
    public static string BuildDirectoryArgument(string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetDirectory);

        return $"/DIR={targetDirectory.TrimEnd('\\', '/')}";
    }

    /// <inheritdoc />
    /// <remarks>
    /// L'installeur lui-même ne publie RIEN : <c>/VERYSILENT</c> n'écrit aucun avancement et le
    /// processus ne rend la main qu'une fois terminé. Ce qu'il fait en revanche, c'est écrire ses
    /// fichiers progressivement dans le dossier <c>/DIR</c>, et cette croissance est observable.
    /// L'avancement publié ici est donc une ESTIMATION, marquée comme telle
    /// (<see cref="GameInstallProgress.IsEstimated"/>) et bornée sous 100 % jusqu'au retour du
    /// processus — voir <see cref="InstallDirectoryGrowthReporter"/> pour le facteur retenu et les
    /// garde-fous. Une estimation étiquetée vaut mieux qu'une barre indéterminée pendant plusieurs
    /// minutes ; un pourcentage inventé et présenté comme exact vaudrait moins que les deux.
    /// </remarks>
    public async Task InstallAsync(
        string archivePath,
        string targetDirectory,
        IProgress<GameInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _fileSystem.Directory.CreateDirectory(targetDirectory);

        var arguments = new List<string>(SilentArguments) { BuildDirectoryArgument(targetDirectory) };
        var request = new ProcessRunRequest(archivePath, arguments);

        _log.Write(AppLogLevel.Info, $"Installeur Vintage Story : {ProcessCommandLine.Render(request)}");

        var result = await RunWatchedAsync(request, archivePath, targetDirectory, progress, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            _log.Write(AppLogLevel.Error, $"Installeur Vintage Story : code de sortie {result.ExitCode}.");

            throw GameInstallFailedException.ForInstallerExitCode(archivePath, result.ExitCode, result.StandardError);
        }

        _log.Write(AppLogLevel.Info, $"Installeur Vintage Story : terminé avec le code 0, cible attendue « {targetDirectory} ».");
    }

    // L'observation s'arrête TOUJOURS avec le processus, succès comme échec : le finally annule la
    // boucle et l'attend, pour qu'aucun échantillon n'arrive après que l'appelant ait tourné la page.
    private async Task<ProcessRunResult> RunWatchedAsync(
        ProcessRunRequest request,
        string archivePath,
        string targetDirectory,
        IProgress<GameInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var reporter = InstallDirectoryGrowthReporter.TryCreate(_fileSystem, archivePath, targetDirectory, progress);
        if (reporter is null)
        {
            return await _processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }

        using var watching = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loop = reporter.RunAsync(_sampleInterval, _delay, watching.Token);
        try
        {
            return await _processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await watching.CancelAsync().ConfigureAwait(false);
            await loop.ConfigureAwait(false);
        }
    }
}