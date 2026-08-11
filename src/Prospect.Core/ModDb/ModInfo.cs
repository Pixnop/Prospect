using Prospect.Core.Common;

namespace Prospect.Core.ModDb;

/// <summary>
/// Côté sur lequel le JEU charge un mod, tel que déclaré par le <c>modinfo.json</c> embarqué dans
/// l'archive. Vocabulaire volontairement distinct de <see cref="ModDbSide"/> (celui de la fiche
/// web) : la recherche a établi que ce sont deux données différentes, pas deux casses de la même,
/// et que seule celle-ci décrit le fichier réellement installé.
/// </summary>
public enum ModSide
{
    /// <summary>Client et serveur. Valeur par défaut quand le champ est absent.</summary>
    Universal,

    /// <summary>Client uniquement.</summary>
    Client,

    /// <summary>Serveur uniquement.</summary>
    Server,
}

/// <summary>Nature du mod, telle que l'énumère <c>EnumModType</c> côté jeu.</summary>
public enum ModType
{
    /// <summary>Valeur absente ou non reconnue.</summary>
    Unknown,

    /// <summary>Pack de thème.</summary>
    Theme,

    /// <summary>Contenu (assets, recettes) sans code.</summary>
    Content,

    /// <summary>Mod à code compilé.</summary>
    Code,
}

/// <summary>
/// Ce que Prospect retient d'un <c>modinfo.json</c>. Les valeurs par défaut suivent exactement la
/// table de docs/research/moddb-api.md, elle-même croisée entre le wiki, la doc XML de l'assembly
/// du jeu et trois échantillons réels.
/// </summary>
public sealed record ModInfo
{
    /// <summary>
    /// Identifiant du mod. Optionnel dans le fichier : dérivé du nom en minuscules sans caractères
    /// spéciaux quand il manque, exactement comme le fait le jeu.
    /// </summary>
    public required string ModId { get; init; }

    /// <summary>Nom affiché.</summary>
    public required string Name { get; init; }

    /// <summary>Version déclarée, ou <see langword="null"/> si absente ou illisible.</summary>
    public ModVersion? Version { get; init; }

    /// <summary>Version telle qu'écrite dans le fichier, y compris quand elle ne se parse pas.</summary>
    public string? RawVersion { get; init; }

    /// <summary>Description libre.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Auteurs. Toujours un tableau dans le fichier, même pour un seul auteur.</summary>
    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>Contributeurs, optionnels.</summary>
    public IReadOnlyList<string> Contributors { get; init; } = [];

    /// <summary>Côté de chargement, <see cref="ModSide.Universal"/> par défaut.</summary>
    public ModSide Side { get; init; } = ModSide.Universal;

    /// <summary>Nature du mod.</summary>
    public ModType Type { get; init; } = ModType.Unknown;

    /// <summary>Requis côté client pour un mod universel. <see langword="true"/> par défaut.</summary>
    public bool RequiredOnClient { get; init; } = true;

    /// <summary>Requis côté serveur pour un mod universel. <see langword="true"/> par défaut.</summary>
    public bool RequiredOnServer { get; init; } = true;

    /// <summary>
    /// Dépendances déclarées, sous la forme <c>{modid: contrainte}</c>. Chaque contrainte est une
    /// BORNE MINIMALE, jamais une version exacte ni un wildcard.
    /// </summary>
    public IReadOnlyDictionary<string, VersionRequirement> Dependencies { get; init; } =
        new Dictionary<string, VersionRequirement>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Contraintes que le parseur n'a pas su lire, conservées pour diagnostic plutôt que jetées.</summary>
    public IReadOnlyList<string> UnreadableDependencies { get; init; } = [];

    /// <summary>Chemin d'icône déclaré, ou <see langword="null"/> (le jeu tente alors <c>modicon.png</c>).</summary>
    public string? IconPath { get; init; }

    /// <summary>Site du mod, optionnel.</summary>
    public string? Website { get; init; }

    /// <summary>
    /// Contrainte sur la version du jeu, extraite de la dépendance spéciale <c>game</c>, ou
    /// <see langword="null"/> si le mod n'en déclare pas.
    /// </summary>
    public VersionRequirement? GameRequirement { get; init; }

    /// <summary>
    /// Dépendances vers de VRAIS mods : <c>game</c>, <c>survival</c> et <c>creative</c> exclus.
    /// Le ModDB lui-même les écarte de sa résolution (<c>IGNORED_AUTO_IDENTIFIERS</c> dans
    /// <c>lib/relations.php</c>), <c>game</c> alimentant la compatibilité de version de jeu et les
    /// deux autres étant du contenu livré avec le jeu.
    /// </summary>
    public IReadOnlyDictionary<string, VersionRequirement> ModDependencies => Dependencies
        .Where(entry => !ModInfoParser.IsSpecialDependencyId(entry.Key))
        .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Pourquoi une archive n'a pas pu donner de <see cref="ModInfo"/> exploitable.</summary>
public enum ModInfoProblem
{
    /// <summary>Aucun problème.</summary>
    None,

    /// <summary>L'archive ne contient pas de <c>modinfo.json</c>.</summary>
    MissingModInfo,

    /// <summary>Le <c>modinfo.json</c> existe mais n'est pas un JSON exploitable, même en mode tolérant.</summary>
    MalformedJson,

    /// <summary>Le JSON est valide mais ne porte ni <c>modid</c> ni <c>name</c> : rien pour identifier le mod.</summary>
    MissingIdentity,

    /// <summary>Le fichier n'est pas une archive zip lisible.</summary>
    UnreadableArchive,
}

/// <summary>
/// Résultat d'une lecture de <c>modinfo.json</c> : soit un <see cref="ModInfo"/>, soit une raison.
/// Volontairement pas une exception : un mod illisible déposé à la main dans <c>data/Mods/</c> doit
/// apparaître dans la liste avec la mention « non identifié », pas faire échouer le scan entier.
/// </summary>
/// <param name="Info">Les métadonnées lues, ou <see langword="null"/> en cas d'échec.</param>
/// <param name="Problem">La raison de l'échec, ou <see cref="ModInfoProblem.None"/>.</param>
public sealed record ModInfoResult(ModInfo? Info, ModInfoProblem Problem)
{
    /// <summary>Vrai si le mod a pu être identifié.</summary>
    public bool IsIdentified => Info is not null;

    /// <summary>Construit un succès.</summary>
    public static ModInfoResult Success(ModInfo info) => new(info, ModInfoProblem.None);

    /// <summary>Construit un échec identifié.</summary>
    public static ModInfoResult Failure(ModInfoProblem problem) => new(null, problem);
}