namespace Prospect.Core.Http;

/// <summary>État d'un téléchargement dans la file.</summary>
public enum DownloadState
{
    /// <summary>Accepté, en attente d'un créneau de téléchargement.</summary>
    Queued,

    /// <summary>Octets en cours de réception.</summary>
    Running,

    /// <summary>Réception terminée, empreinte MD5 en cours de calcul.</summary>
    Verifying,

    /// <summary>Fichier complet et vérifié.</summary>
    Completed,

    /// <summary>Échec définitif (réseau, empreinte incorrecte, disque).</summary>
    Failed,

    /// <summary>Annulé par l'utilisateur.</summary>
    Canceled,
}

/// <summary>
/// Un téléchargement observable dans la file du <see cref="IDownloadManager"/>. C'est l'objet que
/// la vue « Téléchargements » affiche : nom, avancement, vitesse, et un bouton d'annulation qui
/// n'a besoin de rien connaître du moteur.
/// </summary>
public sealed class DownloadOperation
{
    private readonly Action _cancel;

    internal DownloadOperation(string displayName, string fileName, Action cancel)
    {
        Id = Guid.NewGuid();
        DisplayName = displayName;
        FileName = fileName;
        _cancel = cancel;
    }

    /// <summary>Identité de l'opération, stable pour toute sa durée de vie.</summary>
    public Guid Id { get; }

    /// <summary>Libellé lisible, par exemple « Vintage Story 1.22.6 ».</summary>
    public string DisplayName { get; }

    /// <summary>Nom du fichier écrit dans <c>cache/downloads/</c>.</summary>
    public string FileName { get; }

    /// <summary>État courant.</summary>
    public DownloadState State { get; private set; } = DownloadState.Queued;

    /// <summary>Avancement courant.</summary>
    public DownloadProgress Progress { get; private set; } = DownloadProgress.None;

    /// <summary>Message d'échec, renseigné uniquement dans l'état <see cref="DownloadState.Failed"/>.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>Levé à chaque changement d'état ou d'avancement.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Demande l'annulation de ce téléchargement seul. Les autres éléments de la file continuent :
    /// chaque opération a son propre jeton.
    /// </summary>
    public void Cancel() => _cancel();

    internal void SetState(DownloadState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        Raise();
    }

    internal void SetProgress(DownloadProgress progress)
    {
        Progress = progress;
        Raise();
    }

    internal void Fail(string message)
    {
        FailureMessage = message;
        State = DownloadState.Failed;
        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}