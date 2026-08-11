using Prospect.Core.Common;
using Prospect.Core.Instances;

namespace Prospect.Core.Launching;

/// <summary>État suivi d'un lancement.</summary>
public enum RunningInstanceState
{
    /// <summary>Le processus tourne.</summary>
    Started,

    /// <summary>Le processus s'est terminé (normalement ou tué sur demande).</summary>
    Stopped,
}

/// <summary>État observable d'un lancement suivi par <see cref="RunningInstanceTracker"/>, à un instant donné.</summary>
/// <param name="Slug">Instance concernée.</param>
/// <param name="State">Démarré ou arrêté.</param>
/// <param name="ProcessId">Identifiant système du processus, pour affichage/diagnostic.</param>
/// <param name="StartedUtc">Instant de démarrage.</param>
/// <param name="ExitCode">Code de sortie, seulement renseigné une fois <see cref="State"/> à <see cref="RunningInstanceState.Stopped"/>.</param>
/// <param name="StoppedUtc">Instant de sortie, seulement renseigné une fois arrêté.</param>
public sealed record RunningInstanceStatus(
    string Slug,
    RunningInstanceState State,
    int ProcessId,
    DateTimeOffset StartedUtc,
    int? ExitCode,
    DateTimeOffset? StoppedUtc);

/// <summary>
/// Suit les processus de jeu en cours, un par slug d'instance (docs/architecture.md, section
/// « 3. Lancement ») : empêche un double lancement, expose un état observable, et persiste
/// <see cref="InstanceMetadata.LastLaunchedUtc"/> / <see cref="InstanceMetadata.TotalPlaytimeSeconds"/>
/// via <see cref="InstanceService"/>.
/// </summary>
/// <remarks>
/// Décision documentée : <see cref="InstanceMetadata.LastLaunchedUtc"/> est posé au DÉMARRAGE du
/// processus (dans <see cref="TrackStartedAsync"/>), pas à sa sortie. « Dernier lancement » décrit
/// le moment où l'utilisateur a cliqué sur Jouer ; une session encore en cours doit immédiatement
/// remonter en tête du tri par défaut de l'Accueil (<c>HomeSortMode.LastLaunched</c>) sans
/// attendre que le joueur quitte le jeu, ce qui peut prendre des heures.
/// <see cref="InstanceMetadata.TotalPlaytimeSeconds"/>, à l'inverse, n'a de sens qu'à la sortie :
/// c'est la durée réellement écoulée qui est cumulée dans <see cref="ObserveExitAsync"/>.
/// </remarks>
public sealed class RunningInstanceTracker
{
    private readonly InstanceService _instanceService;
    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, TrackedProcess> _tracked = new(StringComparer.Ordinal);

    public RunningInstanceTracker(InstanceService instanceService, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(clock);

        _instanceService = instanceService;
        _clock = clock;
    }

    /// <summary>Levé quand l'état suivi d'une instance change (démarrage ou arrêt).</summary>
    public event EventHandler<RunningInstanceStatus>? StatusChanged;

    /// <summary>Vrai si un processus est actuellement suivi comme démarré pour ce slug.</summary>
    public bool IsRunning(string slug)
    {
        lock (_gate)
        {
            return _tracked.TryGetValue(slug, out var tracked) && tracked.Status.State == RunningInstanceState.Started;
        }
    }

    /// <summary>Dernier état connu pour ce slug, ou <see langword="null"/> si rien n'a jamais été suivi.</summary>
    public RunningInstanceStatus? GetStatus(string slug)
    {
        lock (_gate)
        {
            return _tracked.TryGetValue(slug, out var tracked) ? tracked.Status : null;
        }
    }

    /// <summary>
    /// Enregistre un processus fraîchement démarré par <c>GameLauncher</c> : pose
    /// <see cref="InstanceMetadata.LastLaunchedUtc"/> immédiatement puis observe la sortie en
    /// tâche de fond pour cumuler le playtime, sans bloquer l'appelant jusqu'à ce que le jeu se
    /// termine.
    /// </summary>
    /// <exception cref="InstanceAlreadyRunningException">Cette instance est déjà suivie comme en cours.</exception>
    public async Task<RunningInstanceStatus> TrackStartedAsync(string slug, IRunningProcess process, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentNullException.ThrowIfNull(process);

        if (IsRunning(slug))
        {
            throw new InstanceAlreadyRunningException(slug);
        }

        var record = await _instanceService.MarkLaunchedAsync(slug, cancellationToken).ConfigureAwait(false);
        var startedUtc = record.Metadata.LastLaunchedUtc
            ?? throw new InvalidOperationException("MarkLaunchedAsync aurait dû renseigner LastLaunchedUtc.");
        var status = new RunningInstanceStatus(slug, RunningInstanceState.Started, process.Id, startedUtc, null, null);

        lock (_gate)
        {
            _tracked[slug] = new TrackedProcess(process, status);
        }

        StatusChanged?.Invoke(this, status);
        _ = ObserveExitAsync(slug, process, startedUtc);

        return status;
    }

    /// <summary>
    /// Demande l'arrêt du processus suivi pour <paramref name="slug"/> (tue son arbre de
    /// processus). Sans effet si rien n'est en cours pour ce slug : l'état final (arrêté, code de
    /// sortie) est posé de façon asynchrone par <see cref="ObserveExitAsync"/> une fois le
    /// système d'exploitation ayant effectivement terminé le processus, pas par cette méthode.
    /// </summary>
    public void RequestStop(string slug)
    {
        TrackedProcess? tracked;
        lock (_gate)
        {
            _tracked.TryGetValue(slug, out tracked);
        }

        if (tracked is { Status.State: RunningInstanceState.Started })
        {
            tracked.Process.Kill(includeChildren: true);
        }
    }

    private async Task ObserveExitAsync(string slug, IRunningProcess process, DateTimeOffset startedUtc)
    {
        var exitCode = await process.WaitForExitAsync().ConfigureAwait(false);
        var stoppedUtc = _clock.UtcNow;
        var elapsedSeconds = Math.Max(0L, (long)(stoppedUtc - startedUtc).TotalSeconds);
        var status = new RunningInstanceStatus(slug, RunningInstanceState.Stopped, process.Id, startedUtc, exitCode, stoppedUtc);

        lock (_gate)
        {
            _tracked[slug] = new TrackedProcess(process, status);
        }

        StatusChanged?.Invoke(this, status);
        await _instanceService.AddPlaytimeAsync(slug, elapsedSeconds, CancellationToken.None).ConfigureAwait(false);
        process.Dispose();
    }

    private sealed record TrackedProcess(IRunningProcess Process, RunningInstanceStatus Status);
}