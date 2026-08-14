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

    /// <summary>
    /// Le catalogue DÉJÀ mémorisé, mémoire ou disque, sans jamais toucher au réseau, ou
    /// <see langword="null"/> quand rien n'a encore été relevé sur cette installation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct de <see cref="GetCatalogAsync"/> sur le seul point qui compte pour ses appelants :
    /// celui-ci ne DÉCLENCHE rien. Il sert les écrans qui ont besoin d'un renseignement de confort
    /// sur un mod (son logo) sans avoir la moindre raison d'aller chercher trois mégaoctets et demi
    /// de catalogue pour l'obtenir. L'onglet Mods d'une instance vit d'un scan disque et
    /// n'émettait, avant lui, aucun appel réseau à l'ouverture : lui en faire émettre un pour
    /// décorer ses rangées serait un échange perdant.
    /// </para>
    /// <para>
    /// Le cache PÉRIMÉ est servi sans réserve, contrairement à <see cref="GetCatalogAsync"/> qui
    /// tenterait un relevé d'abord. Un logo de fiche ne se démode pas à l'heure, et l'alternative
    /// n'est pas « un logo plus frais » mais « pas de logo du tout ».
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Annulation.</param>
    /// <returns>
    /// Un catalogue de fraîcheur <see cref="ModDbFreshness.Cached"/> ou
    /// <see cref="ModDbFreshness.Stale"/>, jamais <see cref="ModDbFreshness.Live"/>, ou
    /// <see langword="null"/>. Ne lève pour aucune panne : un cache illisible est un cache absent.
    /// </returns>
    Task<ModDbCatalog?> TryGetCachedCatalogAsync(CancellationToken cancellationToken = default);

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
    /// <see cref="ModDbCompatibilityIndex.Unavailable"/> si l'API n'a pas répondu OU a répondu par
    /// un échec applicatif (<see cref="ModDbApiException"/>) : un badge de compatibilité est un
    /// confort, ni son absence ni la nature de la panne ne doivent vider l'écran de recherche.
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
    /// Interroge <c>/api/updates</c> en UN appel pour tous les mods installés IDENTIFIABLES d'une
    /// instance : ne renvoie que ceux réellement en retard, avec leur dernière release.
    /// </summary>
    /// <param name="installedMods">
    /// Version actuellement installée par <c>modidstr</c> (clé insensible à la casse). Un dictionnaire
    /// vide court-circuite l'appel réseau.
    /// </param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <returns>
    /// Une release par <c>modidstr</c> en retard, indexée par la clé envoyée. L'ABSENCE d'un
    /// <c>modidstr</c> envoyé ne distingue pas « à jour » de « inconnu du ModDB » (docs/research/moddb-api.md) :
    /// c'est à l'appelant de comparer le résultat aux clés de <paramref name="installedMods"/>.
    /// </returns>
    /// <exception cref="ModDbApiException">Requête malformée (400 réel sur cet endpoint).</exception>
    /// <exception cref="ModDbUnavailableException">ModDB injoignable.</exception>
    Task<IReadOnlyDictionary<string, ModDbRelease>> GetUpdatesAsync(
        IReadOnlyDictionary<string, ModVersion> installedMods,
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