using System.IO.Abstractions;

using Prospect.Core.Auth;
using Prospect.Core.Backups;
using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Runtime;
using Prospect.Core.Storage;

namespace Prospect.Core.Launching;

/// <summary>
/// Résultat d'un lancement (<see cref="GameLauncher.LaunchAsync"/>) : l'état suivi du processus tout
/// juste démarré, et si la sauvegarde automatique de pré-lancement a échoué.
/// </summary>
/// <param name="Status">État suivi du processus (voir <see cref="RunningInstanceTracker"/>).</param>
/// <param name="AutoBackupFailed">
/// Vrai si <c>autoBeforeLaunch</c> était activé et que la sauvegarde a échoué : jamais bloquant
/// (le lancement continue toujours), mais c'est ce champ que l'appelant UI inspecte pour décider
/// d'afficher un toast d'avertissement bien visible. Toujours faux si le réglage est désactivé ou
/// si la sauvegarde a réussi — une réussite ne se signale pas, seul l'échec du filet de sécurité
/// mérite d'interrompre l'attention du joueur.
/// </param>
public sealed record LaunchOutcome(RunningInstanceStatus Status, bool AutoBackupFailed);

/// <summary>
/// Construit et démarre la commande de lancement du jeu pour une instance (docs/architecture.md,
/// section « 3. Lancement ») : valide tout ce qui doit l'être avant de spawner un processus,
/// construit la ligne de commande via la stratégie de l'OS courant, prend une sauvegarde
/// automatique si l'instance l'a activé, injecte la session de compte dans le dataPath si quelqu'un
/// est connecté, capture la sortie du processus dans le journal de l'instance, puis délègue le
/// suivi du cycle de vie à <see cref="RunningInstanceTracker"/>. Ne bloque jamais jusqu'à la sortie
/// du jeu : revient dès que le processus est démarré.
/// </summary>
public sealed class GameLauncher
{
    private readonly IInstanceRepository _instances;
    private readonly IInstalledGameVersionRepository _installedVersions;
    private readonly IDotnetLocator _dotnetLocator;
    private readonly RunningInstanceTracker _tracker;
    private readonly IAppLog _log;
    private readonly IGameLaunchStrategy _strategy;
    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly AppPaths _appPaths;
    private readonly IClock _clock;
    private readonly VsAccountService _accounts;
    private readonly ClientSettingsSessionWriter _clientSettings;
    private readonly InstanceBackupService _backups;

    public GameLauncher(
        IInstanceRepository instances,
        IInstalledGameVersionRepository installedVersions,
        IDotnetLocator dotnetLocator,
        RunningInstanceTracker tracker,
        IGameLaunchStrategy strategy,
        IProcessRunner processRunner,
        IFileSystem fileSystem,
        AppPaths appPaths,
        IClock clock,
        VsAccountService accounts,
        ClientSettingsSessionWriter clientSettings,
        InstanceBackupService backups,
        IAppLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(installedVersions);
        ArgumentNullException.ThrowIfNull(dotnetLocator);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(clientSettings);
        ArgumentNullException.ThrowIfNull(backups);

        _instances = instances;
        _installedVersions = installedVersions;
        _dotnetLocator = dotnetLocator;
        _tracker = tracker;
        _strategy = strategy;
        _processRunner = processRunner;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _clock = clock;
        _accounts = accounts;
        _clientSettings = clientSettings;
        _backups = backups;
        _log = log ?? NullAppLog.Instance;
    }

    /// <summary>
    /// Chemin du journal du dernier lancement d'une instance (<c>logs/instance-&lt;slug&gt;.log</c>),
    /// qu'il existe ou non : consommé par l'onglet Journal de la page de détail.
    /// </summary>
    public string GetLogFilePath(string slug)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);

        return _fileSystem.Path.Combine(_appPaths.LogsDirectory, $"instance-{slug}.log");
    }

    /// <summary>
    /// Supprime le journal de lancement d'une instance. Appelé à la suppression de l'instance :
    /// le journal vit sous <c>logs/</c>, DEHORS du dossier de l'instance, donc rien ne l'emportait
    /// avec elle et une instance recréée du même nom affichait le journal de la précédente, entête
    /// et nom d'origine compris.
    /// </summary>
    /// <remarks>Sans effet si le fichier n'existe pas, et silencieux s'il refuse d'être supprimé.</remarks>
    public void DeleteLogFile(string slug)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);

        try
        {
            var path = GetLogFilePath(slug);
            if (_fileSystem.File.Exists(path))
            {
                _fileSystem.File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Un journal encore verrouillé sera écrasé au prochain lancement : rien à signaler.
        }
    }

    /// <summary>
    /// Lance le jeu pour l'instance <paramref name="slug"/>. Valide dans l'ordre : l'instance
    /// n'est pas déjà en cours, elle existe, sa version est installée et complète, le runtime
    /// .NET qu'elle requiert est présent. Toute validation qui échoue lève AVANT qu'un seul
    /// processus ne soit démarré. Une fois les validations passées : sauvegarde automatique si
    /// activée (voir <see cref="RunAutoBackupBeforeLaunchAsync"/>), PUIS injection de session,
    /// PUIS démarrage du processus.
    /// </summary>
    /// <param name="slug">Instance à lancer.</param>
    /// <param name="autoBackupProgress">
    /// Progression de la sauvegarde automatique de pré-lancement, si l'instance l'a activée
    /// (<c>null</c> pour l'ignorer).
    /// </param>
    /// <param name="cancellationToken">
    /// Annulation coopérative. Une annulation pendant la sauvegarde automatique de pré-lancement
    /// annule le lancement entier (le joueur a dit stop) : l'exception se propage sans être
    /// rattrapée, à la différence d'un échec de sauvegarde (voir <see cref="LaunchOutcome.AutoBackupFailed"/>).
    /// </param>
    /// <exception cref="InstanceAlreadyRunningException">Cette instance a déjà une session en cours.</exception>
    /// <exception cref="InstanceNotFoundException">Aucune instance pour ce slug.</exception>
    /// <exception cref="GameVersionNotInstalledException">La version du jeu de cette instance n'est pas installée.</exception>
    /// <exception cref="RuntimeNotAvailableException">Le runtime .NET requis n'est pas installé.</exception>
    /// <exception cref="MacLaunchNotSupportedException">macOS : lancement non pris en charge.</exception>
    public async Task<LaunchOutcome> LaunchAsync(
        string slug,
        IProgress<InstanceBackupProgress>? autoBackupProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);

        if (_tracker.IsRunning(slug))
        {
            throw new InstanceAlreadyRunningException(slug);
        }

        var instance = await _instances.LoadAsync(slug, cancellationToken).ConfigureAwait(false);

        var installed = _installedVersions.Find(instance.Metadata.GameVersion)
            ?? throw GameVersionNotInstalledException.For(instance.Metadata.GameVersion);

        var runtimeCheck = await _dotnetLocator.CheckAsync(installed.Directory, cancellationToken).ConfigureAwait(false);
        if (runtimeCheck.Availability == RuntimeAvailability.Missing)
        {
            throw RuntimeNotAvailableException.For(runtimeCheck.Requirement);
        }

        var executablePath = _strategy.ResolveExecutablePath(installed.Directory);

        var arguments = new List<string> { $"--dataPath={_instances.GetDataDirectory(slug)}" };
        arguments.AddRange(instance.Metadata.Launch.ExtraArgs);

        var logPath = GetLogFilePath(slug);
        PrepareLogFile(logPath, instance);

        var autoBackupFailed = await RunAutoBackupBeforeLaunchAsync(instance, logPath, autoBackupProgress, cancellationToken).ConfigureAwait(false);

        await InjectAccountSessionAsync(slug, logPath, cancellationToken).ConfigureAwait(false);

        // L'environnement passe par la stratégie : c'est elle qui sait ce que SON système ajoute
        // (mesa_glthread sous Linux), et GameLauncher continue d'ignorer sur quoi il tourne.
        var environment = _strategy.BuildEnvironment(instance.Metadata.Launch);
        var request = new ProcessStartRequest(executablePath, arguments, environment, installed.Directory);
        var process = _processRunner.Start(request);
        WireLogCapture(process, logPath);

        var status = await _tracker.TrackStartedAsync(slug, process, cancellationToken).ConfigureAwait(false);

        _log.Write(
            AppLogLevel.Info,
            $"Jeu lancé : instance « {slug} » en {instance.Metadata.GameVersion}, pid {status.ProcessId}, exécutable « {executablePath} ».");

        return new LaunchOutcome(status, autoBackupFailed);
    }

    /// <summary>
    /// Sauvegarde automatique avant lancement (voir <see cref="Backups.InstanceBackupService.CreateAsync"/>),
    /// seulement si <c>autoBeforeLaunch</c> est activé pour cette instance. Deux issues distinctes,
    /// délibérément traitées différemment :
    /// <list type="bullet">
    /// <item>
    /// Un ÉCHEC (disque plein, permissions...) ne bloque JAMAIS le lancement : le joueur voulait
    /// jouer, pas nécessairement se faire sauvegarder, même philosophie que
    /// <see cref="InjectAccountSessionAsync"/>. La raison part dans le journal de l'instance, et
    /// cette méthode rend <see langword="true"/> pour que l'appelant UI le signale en plus par un
    /// toast bien visible (contrairement à l'injection, dont l'échec ne prive que d'un confort
    /// multijoueur, celui-ci prive le joueur de son filet de sécurité — il doit le savoir).
    /// </item>
    /// <item>
    /// Une ANNULATION n'est PAS un échec rattrapé : elle n'est pas interceptée ici, elle se
    /// propage et annule le lancement entier. Le joueur a explicitement demandé d'arrêter pendant
    /// la sauvegarde, ce n'est pas à cette méthode de décider de continuer quand même.
    /// </item>
    /// </list>
    /// </summary>
    private async Task<bool> RunAutoBackupBeforeLaunchAsync(
        InstanceRecord instance,
        string logPath,
        IProgress<InstanceBackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!instance.Metadata.Backups.AutoBeforeLaunch)
        {
            return false;
        }

        try
        {
            await _backups.CreateAsync(instance.Slug, progress, cancellationToken).ConfigureAwait(false);

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppendLogLine(logPath, $"Sauvegarde automatique avant lancement échouée : {exception.Message}");

            return true;
        }
    }

    /// <summary>
    /// Pose la session de compte dans le <c>clientsettings.json</c> du dataPath, juste avant le
    /// spawn, pour que le JEU se considère connecté en multijoueur (docs/research, section d).
    /// Sans compte connecté, ce chemin ne touche rigoureusement à rien : le jeu démarre non
    /// authentifié, exactement comme avant l'existence de cette fonctionnalité.
    /// </summary>
    /// <remarks>
    /// Un échec d'injection ne fait jamais échouer un lancement : le joueur voulait jouer, pas
    /// forcément se connecter, et un fichier de réglages illisible ou un disque plein n'ont pas à
    /// lui interdire son solo. La raison part dans le journal de l'instance — visible depuis
    /// l'onglet Journal — plutôt que d'être avalée en silence comme le fait VS Launcher.
    /// </remarks>
    private async Task InjectAccountSessionAsync(string slug, string logPath, CancellationToken cancellationToken)
    {
        if (_accounts.CurrentSession is not { } session)
        {
            return;
        }

        try
        {
            await _clientSettings
                .WriteAsync(_instances.GetDataDirectory(slug), session, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is CorruptedFileException or IOException or UnauthorizedAccessException)
        {
            AppendLogLine(logPath, $"Session multijoueur non injectée : {exception.Message}");
        }
    }

    // Écrit un en-tête horodaté (IClock, pas DateTimeOffset.UtcNow) puis remplace tout contenu
    // existant : WriteAllText tronque le fichier, un journal ne garde donc que le dernier
    // lancement, jamais l'historique complet des sessions précédentes.
    private void PrepareLogFile(string logPath, InstanceRecord instance)
    {
        var directory = _fileSystem.Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }

        var header = $"[{_clock.UtcNow:O}] Lancement de « {instance.Metadata.Name} » ({instance.Metadata.GameVersion}){Environment.NewLine}";
        _fileSystem.File.WriteAllText(logPath, header);
    }

    private void WireLogCapture(IRunningProcess process, string logPath)
    {
        process.OutputDataReceived += (_, line) => AppendLogLine(logPath, line);
        process.ErrorDataReceived += (_, line) => AppendLogLine(logPath, line);
    }

    private void AppendLogLine(string logPath, string line)
    {
        try
        {
            _fileSystem.File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch (IOException)
        {
            // Une écriture de journal ratée ne doit jamais faire tomber le jeu qu'elle observe :
            // le pire cas est une ligne perdue, pas un lancement interrompu.
        }
    }
}