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