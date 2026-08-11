using System.IO.Abstractions;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Runtime;
using Prospect.Core.Storage;

namespace Prospect.Core.Launching;

/// <summary>
/// Construit et démarre la commande de lancement du jeu pour une instance (docs/architecture.md,
/// section « 3. Lancement ») : valide tout ce qui doit l'être avant de spawner un processus,
/// construit la ligne de commande via la stratégie de l'OS courant, capture la sortie du
/// processus dans le journal de l'instance, puis délègue le suivi du cycle de vie à
/// <see cref="RunningInstanceTracker"/>. Ne bloque jamais jusqu'à la sortie du jeu : revient dès
/// que le processus est démarré.
/// </summary>
public sealed class GameLauncher
{
    private readonly IInstanceRepository _instances;
    private readonly IInstalledGameVersionRepository _installedVersions;
    private readonly IDotnetLocator _dotnetLocator;
    private readonly RunningInstanceTracker _tracker;
    private readonly IGameLaunchStrategy _strategy;
    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly AppPaths _appPaths;
    private readonly IClock _clock;

    public GameLauncher(
        IInstanceRepository instances,
        IInstalledGameVersionRepository installedVersions,
        IDotnetLocator dotnetLocator,
        RunningInstanceTracker tracker,
        IGameLaunchStrategy strategy,
        IProcessRunner processRunner,
        IFileSystem fileSystem,
        AppPaths appPaths,
        IClock clock)
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

        _instances = instances;
        _installedVersions = installedVersions;
        _dotnetLocator = dotnetLocator;
        _tracker = tracker;
        _strategy = strategy;
        _processRunner = processRunner;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _clock = clock;
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
    /// Lance le jeu pour l'instance <paramref name="slug"/>. Valide dans l'ordre : l'instance
    /// n'est pas déjà en cours, elle existe, sa version est installée et complète, le runtime
    /// .NET qu'elle requiert est présent. Toute validation qui échoue lève AVANT qu'un seul
    /// processus ne soit démarré.
    /// </summary>
    /// <exception cref="InstanceAlreadyRunningException">Cette instance a déjà une session en cours.</exception>
    /// <exception cref="InstanceNotFoundException">Aucune instance pour ce slug.</exception>
    /// <exception cref="GameVersionNotInstalledException">La version du jeu de cette instance n'est pas installée.</exception>
    /// <exception cref="RuntimeNotAvailableException">Le runtime .NET requis n'est pas installé.</exception>
    /// <exception cref="MacLaunchNotSupportedException">macOS : lancement non pris en charge.</exception>
    public async Task<RunningInstanceStatus> LaunchAsync(string slug, CancellationToken cancellationToken = default)
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

        var request = new ProcessStartRequest(executablePath, arguments, instance.Metadata.Launch.Env, installed.Directory);
        var process = _processRunner.Start(request);
        WireLogCapture(process, logPath);

        return await _tracker.TrackStartedAsync(slug, process, cancellationToken).ConfigureAwait(false);
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