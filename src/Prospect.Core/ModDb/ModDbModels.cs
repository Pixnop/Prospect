using Prospect.Core.Common;

namespace Prospect.Core.ModDb;

/// <summary>
/// D'où vient la donnée servie par <see cref="IModDbClient"/>, sur le modèle de
/// <see cref="Prospect.Core.GameVersions.GameCatalogFreshness"/> : l'UI a besoin de distinguer un
/// catalogue frais d'un catalogue de repli pour afficher, ou non, le bandeau « ModDB injoignable »
/// de la maquette.
/// </summary>
public enum ModDbFreshness
{
    /// <summary>Réponse obtenue à l'instant depuis l'API.</summary>
    Live,

    /// <summary>Cache encore valide, aucun appel réseau émis.</summary>
    Cached,

    /// <summary>Cache périmé servi parce que l'API n'a pas répondu.</summary>
    Stale,
}

/// <summary>
/// Le côté (client/serveur) tel que l'annonce la FICHE ModDB, choisi à la main par l'auteur dans
/// un menu déroulant du site. Vocabulaire volontairement distinct de <see cref="ModSide"/>, qui
/// est celui du modinfo.json : la recherche a établi que rien ne synchronise les deux et qu'un
/// auteur peut cocher <c>client</c> sur sa fiche alors que son fichier dit <c>Universal</c>.
/// Pour une décision fiable, c'est <see cref="ModSide"/> qui fait foi.
/// </summary>
public enum ModDbSide
{
    /// <summary>Valeur absente ou non reconnue.</summary>
    Unknown,

    /// <summary>Le mod ne s'installe que côté client.</summary>
    Client,

    /// <summary>Le mod ne s'installe que côté serveur.</summary>
    Server,

    /// <summary>Les deux côtés (<c>both</c> côté API, jamais <c>universal</c>).</summary>
    Both,
}

/// <summary>Un tag de catégorie du ModDB (<c>/api/tags</c>), tel que l'affiche la barre de filtres.</summary>
/// <param name="TagId">Identifiant, exposé en chaîne par l'API et conservé tel quel.</param>
/// <param name="Name">Libellé affiché.</param>
public sealed record ModDbTag(string TagId, string Name);

/// <summary>
/// Une entrée du catalogue (<c>/api/mods</c>). Ne porte AUCUNE information de release : cet
/// endpoint n'expose ni fichier ni version de jeu compatible, d'où l'index de compatibilité
/// séparé (<see cref="IModDbClient.GetCompatibilityIndexAsync"/>) et la fiche détaillée
/// (<see cref="IModDbClient.GetModAsync"/>) pour tout le reste.
/// </summary>
public sealed record ModDbModSummary
{
    /// <summary>Identifiant numérique interne, seule clé stable pour rappeler l'API.</summary>
    public required int ModId { get; init; }

    /// <summary>
    /// Identifiant d'asset, celui qui route la page publique. À NE PAS confondre avec
    /// <see cref="ModId"/> : les deux espaces divergent (voir <c>ModDbMapper.BuildPageUrl</c>).
    /// </summary>
    public int AssetId { get; init; }

    /// <summary>Nom affiché.</summary>
    public required string Name { get; init; }

    /// <summary>Résumé d'une ligne.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Auteur principal, tel que le nomme la fiche.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>
    /// Identifiants modinfo.json rattachés à cette fiche. Peut être vide (outils externes) ou
    /// contenir plusieurs entrées (une fiche pour plusieurs mods).
    /// </summary>
    public IReadOnlyList<string> ModIdStrings { get; init; } = [];

    /// <summary>Icône du mod, ou <see langword="null"/> (un tiers du catalogue n'en a pas).</summary>
    public Uri? LogoUrl { get; init; }

    /// <summary>Côté déclaré sur la fiche web, à ne pas confondre avec celui du modinfo.json.</summary>
    public ModDbSide Side { get; init; } = ModDbSide.Unknown;

    /// <summary>Type dérivé de la catégorie : <c>mod</c>, <c>other</c> ou <c>externaltool</c>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Catégories votées, chaînes vides déjà filtrées.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Nombre de téléchargements cumulés.</summary>
    public int Downloads { get; init; }

    /// <summary>Nombre d'abonnés.</summary>
    public int Follows { get; init; }

    /// <summary>Score de tendance calculé par le site (souvent nul).</summary>
    public int TrendingPoints { get; init; }

    /// <summary>Date de la dernière release, ou <see langword="null"/> si illisible ou absente.</summary>
    public DateTimeOffset? LastReleasedUtc { get; init; }

    /// <summary>Page publique de la fiche, alias d'URL quand il existe (voir <c>ModDbMapper.BuildPageUrl</c>).</summary>
    public Uri PageUrl { get; init; } = ModDbMapper.SiteBaseUrl;
}

/// <summary>
/// Une release publiée d'un mod, dans la forme dont Prospect a besoin pour choisir et télécharger.
/// </summary>
public sealed record ModDbRelease
{
    /// <summary>Identifiant de la release, écrit dans la provenance.</summary>
    public required int ReleaseId { get; init; }

    /// <summary>Identifiant du fichier, écrit dans la provenance.</summary>
    public required int FileId { get; init; }

    /// <summary>Identifiant modinfo.json de cette release.</summary>
    public required string ModIdString { get; init; }

    /// <summary>Version du mod, telle que déclarée par l'auteur.</summary>
    public required ModVersion Version { get; init; }

    /// <summary>Nom du fichier publié (jamais une source fiable de version : voir la recherche).</summary>
    public required string FileName { get; init; }

    /// <summary>URL de téléchargement finale (le <c>mainfile</c> de v1 est déjà celle du CDN).</summary>
    public required Uri DownloadUrl { get; init; }

    /// <summary>
    /// Versions de jeu cochées à la main par l'auteur au moment de l'upload. Des versions exactes,
    /// jamais de plage : c'est du déclaratif humain, avec les oublis que ça implique.
    /// </summary>
    public IReadOnlyList<GameVersion> CompatibleGameVersions { get; init; } = [];

    /// <summary>Chaînes de version telles que reçues, y compris celles que notre grammaire ne sait pas lire.</summary>
    public IReadOnlyList<string> CompatibleGameVersionTags { get; init; } = [];

    /// <summary>Date de publication, ou <see langword="null"/> si illisible.</summary>
    public DateTimeOffset? CreatedUtc { get; init; }

    /// <summary>Nombre de téléchargements de cette release.</summary>
    public int Downloads { get; init; }

    /// <summary>Notes de version, en HTML brut, souvent absentes.</summary>
    public string? Changelog { get; init; }
}

/// <summary>Fiche complète d'un mod (<c>/api/mod/{id}</c>), releases comprises.</summary>
public sealed record ModDbModDetail
{
    /// <summary>Identifiant numérique interne.</summary>
    public required int ModId { get; init; }

    /// <summary>
    /// Identifiant d'asset, celui qui route la page publique. À NE PAS confondre avec
    /// <see cref="ModId"/> : les deux espaces divergent (voir <c>ModDbMapper.BuildPageUrl</c>).
    /// </summary>
    public int AssetId { get; init; }

    /// <summary>Nom affiché.</summary>
    public required string Name { get; init; }

    /// <summary>Auteur principal.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>
    /// Description longue en HTML brut d'éditeur riche, telle que publiée. Elle est RENDUE par la
    /// fiche, pas aplatie : <see cref="HtmlRichTextParser.Parse"/> la traduit en blocs
    /// (paragraphes, titres, listes, code, images, liens) que la couche UI compose en contrôles.
    /// <see cref="HtmlText.ToPlainText"/> reste disponible pour les usages qui veulent du texte nu.
    /// </summary>
    public string DescriptionHtml { get; init; } = string.Empty;

    /// <summary>Icône du mod, ou <see langword="null"/>.</summary>
    public Uri? LogoUrl { get; init; }

    /// <summary>Côté déclaré sur la fiche web.</summary>
    public ModDbSide Side { get; init; } = ModDbSide.Unknown;

    /// <summary>Catégories votées.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Nombre de téléchargements cumulés.</summary>
    public int Downloads { get; init; }

    /// <summary>Nombre d'abonnés à la fiche.</summary>
    public int Follows { get; init; }

    /// <summary>Page publique du mod sur le ModDB.</summary>
    public required Uri PageUrl { get; init; }

    /// <summary>
    /// Site du mod, ou <see langword="null"/>. Les quatre liens externes de la fiche sont
    /// nullables ET souvent servis en chaîne VIDE plutôt qu'en <c>null</c>
    /// (docs/research/moddb-api.md) : le mapper traite les deux cas de la même façon.
    /// </summary>
    public Uri? HomepageUrl { get; init; }

    /// <summary>Dépôt de code, ou <see langword="null"/>.</summary>
    public Uri? SourceCodeUrl { get; init; }

    /// <summary>Suivi des tickets, ou <see langword="null"/>.</summary>
    public Uri? IssueTrackerUrl { get; init; }

    /// <summary>Wiki du mod, ou <see langword="null"/>.</summary>
    public Uri? WikiUrl { get; init; }

    /// <summary>Releases, de la plus récente à la plus ancienne.</summary>
    public IReadOnlyList<ModDbRelease> Releases { get; init; } = [];
}

/// <summary>Le catalogue complet, avec la fraîcheur de la donnée servie.</summary>
/// <param name="Mods">Toutes les fiches, dans l'ordre rendu par l'API.</param>
/// <param name="RetrievedUtc">Date du relevé qui a produit ces données.</param>
/// <param name="Freshness">Réseau, cache valide ou cache périmé.</param>
public sealed record ModDbCatalog(
    IReadOnlyList<ModDbModSummary> Mods,
    DateTimeOffset RetrievedUtc,
    ModDbFreshness Freshness);

/// <summary>
/// L'ensemble des mods qui ont au moins une release taguée pour une version de jeu donnée.
/// </summary>
/// <remarks>
/// Cet index existe parce que <c>/api/mods</c> n'expose aucune information de release : sans lui,
/// impossible de badger un résultat de recherche « compatible » ou « incompatible » sans ouvrir
/// la fiche de chaque mod. Il est obtenu par un appel filtré côté serveur (<c>gv=</c> pour une
/// version exacte, <c>gameversion=</c> pour toute une série mineure), le seul endroit où le
/// filtrage n'est pas fait en mémoire : le serveur est le seul à posséder la table de
/// correspondance release/version de jeu.
/// </remarks>
/// <param name="ModIds">Identifiants numériques des mods compatibles.</param>
/// <param name="IsApproximate">Vrai quand l'index couvre toute la série mineure, pas la version exacte.</param>
/// <param name="Freshness">Réseau ou repli.</param>
public sealed record ModDbCompatibilityIndex(
    IReadOnlySet<int> ModIds,
    bool IsApproximate,
    ModDbFreshness Freshness)
{
    /// <summary>Index vide, servi quand l'API est injoignable : aucun badge plutôt qu'un badge faux.</summary>
    public static ModDbCompatibilityIndex Unavailable { get; } =
        new(new HashSet<int>(), IsApproximate: false, ModDbFreshness.Stale);

    /// <summary>Vrai si ce mod a une release taguée pour la version couverte par l'index.</summary>
    public bool Contains(int modId) => ModIds.Contains(modId);
}

/// <summary>
/// Ce que rend <c>install-information</c> pour un identifiant : la version conseillée pour la
/// version de jeu cible, et les identifiants que le serveur dit devoir résoudre en plus.
/// </summary>
/// <param name="RecommendedVersion">Version conseillée par le serveur, ou <see langword="null"/>.</param>
/// <param name="ResolvedDependencies">Identifiants supplémentaires signalés par <c>resolve-deps</c>.</param>
/// <param name="Warnings">Avertissements bruts du serveur, remontés tels quels pour diagnostic.</param>
public sealed record ModDbInstallInformation(
    ModVersion? RecommendedVersion,
    IReadOnlyList<string> ResolvedDependencies,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Réponse vide, utilisée quand l'endpoint n'a rien à dire ou n'a pas répondu.</summary>
    public static ModDbInstallInformation Empty { get; } = new(null, [], []);
}