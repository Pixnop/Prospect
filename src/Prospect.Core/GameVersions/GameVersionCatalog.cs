namespace Prospect.Core.GameVersions;

/// <summary>
/// Provenance des données rendues par <see cref="IGameVersionCatalog"/>. L'interface l'expose
/// plutôt que de la garder pour elle : « je n'ai pas pu joindre l'API, voici ce que je savais
/// hier » est une information que l'utilisateur doit voir, pas un détail d'implémentation.
/// </summary>
public enum GameCatalogFreshness
{
    /// <summary>Réponse obtenue à l'instant auprès de l'API officielle.</summary>
    Live,

    /// <summary>Cache encore valide (dans son TTL) : aucun appel réseau n'a été tenté.</summary>
    Cached,

    /// <summary>Cache périmé servi en repli parce que l'API est injoignable.</summary>
    Stale,
}

/// <summary>
/// Résultat d'une consultation du catalogue : les versions connues, la date à laquelle ces
/// données ont été obtenues, et leur fraîcheur.
/// </summary>
/// <param name="Versions">Versions publiées, triées par ordre décroissant.</param>
/// <param name="RetrievedUtc">Date de l'appel réseau qui a produit ces données.</param>
/// <param name="Freshness">Provenance de ces données.</param>
public sealed record GameVersionCatalog(
    IReadOnlyList<GameVersionCatalogEntry> Versions,
    DateTimeOffset RetrievedUtc,
    GameCatalogFreshness Freshness);