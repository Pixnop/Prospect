using System.Text.Json.Serialization;

using Prospect.Core.Common;

namespace Prospect.Core.ModDb;

/// <summary>
/// Ce que Prospect sait de l'origine ModDB d'un mod qu'il a installé lui-même : de quelle fiche,
/// de quelle release et de quel fichier il vient.
/// </summary>
/// <remarks>
/// Cette information n'est PAS dérivable du zip : le <c>modinfo.json</c> connaît le <c>modid</c> et
/// la version, jamais le <c>releaseid</c>, le <c>fileid</c> ni l'identifiant numérique de la fiche.
/// C'est ce qui rend la future détection de mises à jour exacte plutôt qu'approximative. Le fichier
/// qui la stocke reste un CACHE reconstructible : le supprimer fait retomber la correspondance sur
/// le seul <c>modid</c>, ce qui dégrade sans rien casser (docs/architecture.md, prospect-mods.json).
/// </remarks>
public sealed record ModProvenance
{
    /// <summary>Nom du fichier dans <c>data/Mods/</c>, clé de correspondance avec le disque.</summary>
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    /// <summary>Identifiant numérique de la fiche ModDB.</summary>
    [JsonPropertyName("modId")]
    public required int ModId { get; init; }

    /// <summary>Identifiant modinfo.json de la release installée.</summary>
    [JsonPropertyName("modIdString")]
    public required string ModIdString { get; init; }

    /// <summary>Identifiant de la release.</summary>
    [JsonPropertyName("releaseId")]
    public required int ReleaseId { get; init; }

    /// <summary>Identifiant du fichier téléchargé.</summary>
    [JsonPropertyName("fileId")]
    public required int FileId { get; init; }

    /// <summary>Version installée au moment de l'installation.</summary>
    [JsonPropertyName("version")]
    public required ModVersion Version { get; init; }

    /// <summary>Date d'installation.</summary>
    [JsonPropertyName("installedUtc")]
    public required DateTimeOffset InstalledUtc { get; init; }

    /// <summary>
    /// Vrai si la release a été retenue par élargissement à la série mineure plutôt que par tag
    /// exact. Conservé parce que c'est une compatibilité supposée, pas déclarée par l'auteur.
    /// </summary>
    [JsonPropertyName("approximateMatch")]
    public bool ApproximateMatch { get; init; }

    /// <summary>
    /// Vrai quand l'utilisateur a explicitement installé une release dont l'auteur n'a coché
    /// AUCUNE version de la série de jeu de l'instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct d'<see cref="ApproximateMatch"/>, qui ne couvre que le rapprochement de série
    /// mineure : une release taguée 1.22.4 posée sur une instance 1.22.6 reste un pari raisonnable,
    /// une release taguée 1.20 posée sur du 1.22 est une décision assumée. Écrire le même drapeau
    /// pour les deux ferait passer la seconde pour la première.
    /// </para>
    /// <para>
    /// Ce que le docteur d'instance en fera : sa vérification de compatibilité des mods
    /// (<c>InstanceDoctor</c>, <c>ModCompatibilityDoctorResult</c>) compte aujourd'hui les mods
    /// incompatibles à partir des tags. Avec ce champ, elle pourra distinguer un mod qu'on a
    /// installé EN LE SACHANT d'un mod devenu incompatible parce que l'instance a changé de version
    /// de jeu depuis — deux constats qui n'appellent pas la même action. Le champ est écrit dès
    /// maintenant pour que l'historique existe le jour où cette lecture arrivera ; rien ne le lit
    /// encore, et ce fichier reste un cache reconstructible.
    /// </para>
    /// <para>
    /// Piège <c>System.Text.Json</c> : un <c>prospect-mods.json</c> écrit avant ce champ ne le
    /// porte pas, et l'initialiseur d'une propriété <c>init</c> n'est PAS rejoué quand la clé
    /// manque. C'est sans danger ici, et seulement ici, parce que la valeur voulue pour une entrée
    /// ancienne est exactement le défaut du type (<see langword="false"/> : aucune installation
    /// passée n'a pu être faite en connaissance de cause, l'option n'existait pas). Tout champ dont
    /// le défaut souhaité ne serait pas celui du type devra passer par une normalisation, comme le
    /// fait <c>ProspectSettings.Normalized()</c>.
    /// </para>
    /// </remarks>
    [JsonPropertyName("declaredIncompatible")]
    public bool DeclaredIncompatible { get; init; }
}

/// <summary>Contenu du fichier <c>prospect-mods.json</c> d'une instance.</summary>
public sealed record ModProvenanceDocument
{
    /// <summary>Version de schéma courante.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Version du schéma du document lu.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Une entrée par mod installé par Prospect.</summary>
    [JsonPropertyName("mods")]
    public IReadOnlyList<ModProvenance> Mods { get; init; } = [];
}

/// <summary>Contexte source-gen du fichier de provenance.</summary>
[JsonSerializable(typeof(ModProvenanceDocument))]
internal sealed partial class ModProvenanceJsonContext : JsonSerializerContext;