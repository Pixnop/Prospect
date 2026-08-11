using Prospect.Core.Common;

namespace Prospect.Core.ModDb;

/// <summary>
/// Port vers l'API publique du ModDB Vintage Story (<c>mods.vintagestory.at/api</c>), sans aucune
/// authentification : tout ce que consomme Prospect vit dans les endpoints anonymes documentés par
/// docs/research/moddb-api.md.
/// </summary>
public interface IModDbClient
{
    /// <summary>
    /// Le catalogue complet. Il n'existe aucune pagination côté serveur : cet appel rend les
    /// ~8 000 fiches d'un coup (3,5 Mo), d'où le cache mémoire + disque et la recherche filtrée
    /// en mémoire par <see cref="ModCatalogQuery"/>.
    /// </summary>
    /// <param name="forceRefresh">Ignore le cache et interroge l'API.</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <exception cref="ModDbUnavailableException">API injoignable et aucun cache exploitable.</exception>
    Task<ModDbCatalog> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>Le vocabulaire des catégories (<c>/api/tags</c>), pour la barre de filtres.</summary>
    /// <exception cref="ModDbUnavailableException">API injoignable et aucun cache exploitable.</exception>
    Task<IReadOnlyList<ModDbTag>> GetTagsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// La fiche complète d'un mod, releases comprises. <paramref name="modIdOrIdentifier"/> accepte
    /// aussi bien l'identifiant numérique interne que le <c>modid</c> d'un modinfo.json (les deux
    /// sont résolus par le même endpoint), mais jamais un <c>urlalias</c> de page web, qui est une
    /// troisième donnée distincte.
    /// </summary>
    /// <exception cref="ModDbApiException">Mod inconnu (<c>statuscode</c> 404 dans un corps HTTP 200).</exception>
    /// <exception cref="ModDbUnavailableException">API injoignable.</exception>
    Task<ModDbModDetail> GetModAsync(string modIdOrIdentifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// L'ensemble des mods ayant une release taguée pour <paramref name="gameVersion"/>. Le seul
    /// filtre délégué au serveur, faute d'information de release dans <c>/api/mods</c>.
    /// </summary>
    /// <param name="gameVersion">Version de jeu cible.</param>
    /// <param name="widenToMinorSeries">
    /// Vrai pour couvrir toute la série <c>Major.Minor</c> au lieu de la version exacte. Résultat
    /// approximatif, à signaler comme tel dans l'UI : il repose sur l'hypothèse qu'un auteur a pu
    /// oublier de cocher la version exacte.
    /// </param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <returns>
    /// <see cref="ModDbCompatibilityIndex.Unavailable"/> si l'API n'a pas répondu : un badge de
    /// compatibilité est un confort, son absence ne doit pas vider l'écran de recherche.
    /// </returns>
    Task<ModDbCompatibilityIndex> GetCompatibilityIndexAsync(
        GameVersion gameVersion,
        bool widenToMinorSeries = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Interroge <c>/api/v2/mods/install-information</c> avec <c>resolve-deps=true</c> pour une
    /// liste d'identifiants et une version de jeu cible. Endpoint v2 : ses codes HTTP sont fiables,
    /// contrairement à ceux de v1.
    /// </summary>
    /// <returns>
    /// Une entrée par identifiant reconnu. Un dictionnaire vide en cas d'échec : cette source est
    /// un complément, la vérification locale du modinfo.json reste l'autorité.
    /// </returns>
    Task<IReadOnlyDictionary<string, ModDbInstallInformation>> GetInstallInformationAsync(
        IReadOnlyList<string> identifiers,
        GameVersion gameVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Taille annoncée par le CDN pour une URL de fichier (requête <c>HEAD</c>,
    /// <c>Content-Length</c>), ou <see langword="null"/> si elle n'est pas annoncée.
    /// </summary>
    /// <remarks>
    /// L'API du ModDB n'expose ni taille ni empreinte pour ses fichiers, la recherche l'a vérifié
    /// dans tout <c>lib/api/</c> : ce <c>HEAD</c> est le seul garde-fou disponible avant et après
    /// un téléchargement de mod.
    /// </remarks>
    Task<long?> GetFileSizeAsync(Uri fileUrl, CancellationToken cancellationToken = default);
}