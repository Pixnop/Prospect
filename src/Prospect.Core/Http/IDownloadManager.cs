namespace Prospect.Core.Http;

/// <summary>
/// File de téléchargements partagée par tout ce qui rapatrie un fichier : les versions du jeu
/// aujourd'hui, les mods demain (docs/architecture.md, « Transverse : téléchargements »). Le
/// popover « Téléchargements » de la barre latérale n'est qu'une vue sur cette file.
/// </summary>
public interface IDownloadManager
{
    /// <summary>
    /// Téléchargements en cours, en attente, et terminés — réussis, échoués ou annulés. Rien ne
    /// sort de la file tout seul tant qu'une opération est vivante ; une opération terminée y reste
    /// jusqu'à ce que l'utilisateur l'écarte ou qu'une plus récente la pousse dehors
    /// (<see cref="DownloadOptions.HistoryLimit"/>).
    ///
    /// L'historique est de SESSION : rien n'est écrit sur disque, et fermer Prospect l'efface.
    /// </summary>
    IReadOnlyList<DownloadOperation> Operations { get; }

    /// <summary>Levé quand la composition de la file change (ajout, retrait).</summary>
    event EventHandler? OperationsChanged;

    /// <summary>
    /// Télécharge un fichier dans <c>cache/downloads/</c> et rend son chemin final. Le fichier
    /// est écrit en flux dans un <c>.partial</c>, repris par en-tête <c>Range</c> après une
    /// coupure, vérifié contre l'empreinte annoncée, puis renommé en un seul mouvement.
    /// </summary>
    /// <param name="request">Ce qu'il faut télécharger et depuis quels miroirs.</param>
    /// <param name="progress">Observateur d'avancement, facultatif (l'opération de la file en expose déjà un).</param>
    /// <param name="cancellationToken">Annulation ; le fichier partiel est alors supprimé.</param>
    /// <exception cref="DownloadChecksumMismatchException">Le fichier reçu ne correspond pas à l'empreinte annoncée.</exception>
    /// <exception cref="DownloadFailedException">Tous les miroirs ont échoué.</exception>
    Task<string> DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Retire de la file une opération, terminée ou non.</summary>
    void Dismiss(DownloadOperation operation);

    /// <summary>
    /// Vide l'historique : retire toutes les opérations terminées et laisse les vivantes. C'est le
    /// « Tout effacer » du popover, qui ne doit jamais emporter un téléchargement en cours.
    /// </summary>
    void DismissFinished();
}