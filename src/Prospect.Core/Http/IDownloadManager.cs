namespace Prospect.Core.Http;

/// <summary>
/// File de téléchargements partagée par tout ce qui rapatrie un fichier : les versions du jeu
/// aujourd'hui, les mods demain (docs/architecture.md, « Transverse : téléchargements »). Le
/// popover « Téléchargements » de la barre latérale n'est qu'une vue sur cette file.
/// </summary>
public interface IDownloadManager
{
    /// <summary>
    /// Téléchargements en cours, en attente, ou terminés en échec. Un téléchargement réussi sort
    /// de la file de lui-même ; un échec y reste jusqu'à ce que l'utilisateur l'écarte, pour qu'il
    /// ait le temps d'en lire la raison.
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

    /// <summary>Retire de la file une opération terminée (échec ou annulation).</summary>
    void Dismiss(DownloadOperation operation);
}