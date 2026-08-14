namespace Prospect.Desktop.Services;

/// <summary>
/// Où trouver le logo d'un mod qu'on ne connaît que par son identifiant de fiche ModDB. Complément
/// strict d'<see cref="IModLogoCache"/> : celui-ci répond « quelle URL », l'autre « quels pixels »,
/// et aucune vignette ne s'affiche sans les deux.
/// </summary>
/// <remarks>
/// Le navigateur de mods n'en a pas besoin, parce qu'il tient déjà l'entrée de catalogue de chaque
/// carte. Tous les autres écrans qui nomment un mod n'en ont qu'un identifiant : la provenance d'un
/// mod installé (<c>ModProvenance.ModId</c>) ou l'élément d'un plan (<c>ModInstallItem.ModDbModId</c>).
/// C'est ce port qui les relie au catalogue, sans qu'ils aient à le connaître ni à savoir qu'il est
/// mis en cache.
/// </remarks>
public interface IModLogoDirectory
{
    /// <summary>
    /// L'URL du logo de cette fiche, ou <see langword="null"/> quand il n'y en a pas à afficher.
    /// </summary>
    /// <param name="modDbModId">Identifiant numérique de fiche ModDB.</param>
    /// <param name="cancellationToken">Annulation côté appelant (une rangée remplacée par un rescan).</param>
    /// <returns>
    /// <see langword="null"/> couvre indistinctement quatre cas, et c'est volontaire : la fiche n'a
    /// pas de logo, elle n'est pas dans le catalogue mémorisé, aucun catalogue n'a jamais été relevé
    /// sur cette installation, ou le catalogue mémorisé est illisible. Aucun n'est une anomalie et
    /// tous se rendent pareil, par le pictogramme générique.
    /// </returns>
    Task<Uri?> FindAsync(int modDbModId, CancellationToken cancellationToken = default);
}