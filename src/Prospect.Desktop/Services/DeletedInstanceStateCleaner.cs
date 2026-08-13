using Prospect.Core.Instances;
using Prospect.Core.Launching;

namespace Prospect.Desktop.Services;

/// <summary>
/// Oublie tout l'état PAR SLUG qu'une instance supprimée laisserait derrière elle.
/// </summary>
/// <remarks>
/// <para>
/// Le défaut rapporté en test réel : « supprimer puis recréer une instance du même nom cause des
/// problèmes ». La cause est mécanique. Le slug d'une instance se déduit de son nom et redevient
/// libre dès que son dossier disparaît, si bien qu'une nouvelle instance du même nom reçoit
/// EXACTEMENT le même slug — et hérite alors de tout ce que le launcher gardait en mémoire, ou
/// ailleurs sur le disque, sous cette clé : le rapport de vérification de mises à jour de
/// l'ancienne, son état de processus suivi, son journal de lancement, et la lecture qui avait été
/// faite de ce journal (<see cref="IGameLogInsightsCache"/>).
/// </para>
/// <para>
/// Un objet dédié plutôt qu'un abonnement disséminé : le jour où un cinquième état par slug
/// apparaît, c'est ici qu'on regarde. Il s'abonne à <see cref="InstanceService.Deleted"/>, seul
/// signal fiable puisqu'il est publié une fois la suppression réellement terminée.
/// </para>
/// </remarks>
public sealed class DeletedInstanceStateCleaner : IDisposable
{
    private readonly InstanceService _instanceService;
    private readonly RunningInstanceTracker _tracker;
    private readonly GameLauncher _launcher;
    private readonly IModUpdateCheckCache _updateCache;
    private readonly IGameLogInsightsCache _logInsightsCache;

    /// <summary>Construit le nettoyeur et s'abonne aux suppressions.</summary>
    public DeletedInstanceStateCleaner(
        InstanceService instanceService,
        RunningInstanceTracker tracker,
        GameLauncher launcher,
        IModUpdateCheckCache updateCache,
        IGameLogInsightsCache logInsightsCache)
    {
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(updateCache);
        ArgumentNullException.ThrowIfNull(logInsightsCache);

        _instanceService = instanceService;
        _tracker = tracker;
        _launcher = launcher;
        _updateCache = updateCache;
        _logInsightsCache = logInsightsCache;

        _instanceService.Deleted += OnDeleted;
    }

    /// <inheritdoc />
    public void Dispose() => _instanceService.Deleted -= OnDeleted;

    private void OnDeleted(object? sender, string slug)
    {
        _updateCache.Invalidate(slug);
        _logInsightsCache.Invalidate(slug);
        _tracker.Forget(slug);
        _launcher.DeleteLogFile(slug);
    }
}