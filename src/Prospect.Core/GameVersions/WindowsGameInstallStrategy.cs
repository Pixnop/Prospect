using System.IO.Abstractions;

using Prospect.Core.Common;
using Prospect.Core.GameVersions.Inno;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Installation Windows : extraction du contenu de l'installeur officiel, et exécution de cet
/// installeur seulement si l'extraction n'aboutit pas.
/// </summary>
/// <remarks>
/// <para>
/// Il n'existe aucun build Windows portable, uniquement un installeur Inno Setup
/// (docs/research/vslauncher-et-distribution.md, section b). Le lancer, c'est faire tourner son
/// SCRIPT, et ce script ouvre une boîte de dialogue qu'aucun drapeau de ligne de commande
/// n'atteint : <c>/SUPPRESSMSGBOXES</c> ne couvre que les messages de Setup lui-même et la fonction
/// <c>SuppressibleMsgBox</c> du langage de script, alors que la question « une ancienne version a
/// été détectée, la désinstaller d'abord ? » vient d'un <c>MsgBox</c> nu appelé depuis
/// <c>InitializeSetup</c>. Pire, le script teste une clé de registre que l'installeur écrit
/// lui-même : chaque installation armait la boîte pour la suivante.
/// </para>
/// <para>
/// La voie normale est donc de ne PAS l'exécuter. <see cref="InnoPayloadExtractor"/> lit le format
/// de l'installeur et pose les fichiers du jeu directement. Aucun script ne tourne, donc aucune
/// boîte ne s'ouvre et aucune clé n'est écrite — ce dernier point compte autant que le premier,
/// puisque c'est lui qui empêche le problème de revenir.
/// </para>
/// <para>
/// Le repli reste l'exécution silencieuse, avec le jeu d'options éprouvé par VS Launcher, où
/// <c>/CURRENTUSER</c> évite l'élévation UAC et <c>/DIR</c> pose le jeu dans le dossier de la
/// version. Il sert le jour où l'installeur change de format au point que le lecteur ne le
/// reconnaît plus — un Inno Setup 6.5 réorganise franchement son en-tête — et ce jour-là mieux vaut
/// une installation avec une notice qu'une installation impossible. La notice qui prévient de la
/// boîte n'accompagne plus que ce chemin-là.
/// </para>
/// <para>
/// Dans les deux cas, la vérification qui tranche reste celle du RÉSULTAT, faite par
/// <see cref="GameInstallService"/> avant d'écrire la sentinelle de complétude : une installation
/// détournée ailleurs ne peut pas se faire passer pour réussie.
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

        if (await TryExtractAsync(archivePath, targetDirectory, progress, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await RunInstallerAsync(archivePath, targetDirectory, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tente l'extraction. Rend <see langword="false"/> quand l'installeur n'est pas lisible, sans
    /// faire échouer l'installation : c'est au repli de prendre la suite.
    /// </summary>
    /// <remarks>
    /// Le dossier cible est VIDÉ avant de passer la main. Une extraction interrompue en cours de
    /// route y a laissé des milliers de fichiers, et l'installeur officiel écrirait par-dessus sans
    /// rien nettoyer : le résultat serait un mélange de deux versions du jeu, exactement le genre
    /// d'installation à moitié faite que la sentinelle de complétude cherche à empêcher.
    /// </remarks>
    private async Task<bool> TryExtractAsync(
        string archivePath,
        string targetDirectory,
        IProgress<GameInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await new InnoPayloadExtractor(_fileSystem)
                .ExtractAsync(archivePath, targetDirectory, progress, cancellationToken)
                .ConfigureAwait(false);

            _log.Write(AppLogLevel.Info, "Installeur Vintage Story : contenu extrait sans exécuter l'installeur.");

            return true;
        }
        // Les deux familles d'échec qui veulent dire « nous n'avons pas su le lire nous-mêmes » :
        // un format que le lecteur ne reconnaît pas, et un fichier qui ne se laisse pas ouvrir.
        // Toutes deux passent la main au chemin éprouvé plutôt que de faire échouer l'installation.
        // L'annulation, elle, n'est pas un échec et doit continuer de remonter telle quelle.
        catch (Exception exception) when (exception is InnoFormatException or IOException)
        {
            _log.Write(
                AppLogLevel.Warning,
                $"Extraction de l'installeur impossible ({exception.Message}) : repli sur l'exécution silencieuse.");

            ClearTargetDirectory(targetDirectory);

            return false;
        }
    }

    private void ClearTargetDirectory(string targetDirectory)
    {
        if (!_fileSystem.Directory.Exists(targetDirectory))
        {
            return;
        }

        foreach (var directory in _fileSystem.Directory.GetDirectories(targetDirectory))
        {
            _fileSystem.Directory.Delete(directory, recursive: true);
        }

        foreach (var file in _fileSystem.Directory.GetFiles(targetDirectory))
        {
            _fileSystem.File.Delete(file);
        }
    }

    private async Task RunInstallerAsync(
        string archivePath,
        string targetDirectory,
        IProgress<GameInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Publié AVANT le lancement : la boîte de dialogue de l'installeur peut s'ouvrir dès la
        // première seconde, et une notice qui arriverait après elle ne servirait à rien.
        progress?.Report(GameInstallProgress.ForVendorInstaller());

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