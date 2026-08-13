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
/// <remarks>
/// Une opération survit à sa propre fin. Elle passe dans un état terminal (terminé, échoué,
/// annulé), garde l'instant où c'est arrivé, et reste dans la file jusqu'à ce que l'utilisateur la
/// retire ou qu'une plus récente la pousse dehors. C'est ce qui donne au popover un historique de
/// session : sans cela, un téléchargement réussi disparaissait à la seconde où il aboutissait, et
/// rien ne disait qu'il avait eu lieu.
/// </remarks>
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

    /// <summary>
    /// Instant où l'opération a atteint son état terminal, <see langword="null"/> tant qu'elle est
    /// vivante. C'est ce qui permet d'écrire « il y a 3 min » sur une ligne d'historique.
    /// </summary>
    public DateTimeOffset? FinishedUtc { get; private set; }

    /// <summary>Vrai dès que plus rien ne bougera : terminé, échoué ou annulé.</summary>
    public bool IsFinished => State is DownloadState.Completed or DownloadState.Failed or DownloadState.Canceled;

    /// <summary>
    /// Taille du fichier reçu, telle que le dernier avancement l'a rapportée. Pour un
    /// téléchargement abouti, c'est la taille réelle ; pour un abandon, ce qui avait été reçu.
    /// </summary>
    public long ReceivedBytes => Progress.ReceivedBytes;

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

    /// <summary>
    /// Fige l'instant de fin. Appelé par le gestionnaire, qui est le seul à tenir une horloge :
    /// l'opération n'en a pas, et n'a pas à en avoir une.
    /// </summary>
    internal void MarkFinished(DateTimeOffset finishedUtc)
    {
        FinishedUtc ??= finishedUtc;
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