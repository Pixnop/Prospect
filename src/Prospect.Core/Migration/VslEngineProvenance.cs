using System.Text.Json.Serialization;

namespace Prospect.Core.Migration;

/// <summary>
/// Marqueur posé à côté de la sentinelle <c>.prospect-complete</c> pour un moteur adopté depuis VS
/// Launcher plutôt que téléchargé et vérifié par Prospect (docstring de
/// <see cref="VslAdoptionService"/> pour le pari assumé). Fichier strictement ADDITIF : aucun autre
/// composant du domaine <c>GameVersions</c> ne le lit aujourd'hui, sa présence ne change rien au
/// comportement du reste du launcher, elle documente seulement une origine pour qui inspecte le
/// dossier à la main ou pour une future UI qui voudrait l'afficher.
/// </summary>
public sealed record VslEngineProvenance
{
    /// <summary>Nom du fichier posé dans le dossier de la version installée.</summary>
    public const string FileName = ".prospect-vsl-provenance.json";

    [JsonPropertyName("source")]
    public string Source { get; init; } = "vslauncher";

    /// <summary>Chemin d'origine du moteur dans l'installation VS Launcher, à titre de traçabilité.</summary>
    [JsonPropertyName("sourcePath")]
    public required string SourcePath { get; init; }

    /// <summary>Date de l'adoption (copie), pas la date d'installation originale côté VSL, inconnue.</summary>
    [JsonPropertyName("adoptedUtc")]
    public required DateTimeOffset AdoptedUtc { get; init; }
}

/// <summary>Contexte source-gen des types JSON écrits par le domaine Migration (docs/architecture.md, sérialisation).</summary>
[JsonSerializable(typeof(VslInstallationDto))]
[JsonSerializable(typeof(VslGameVersionDto))]
[JsonSerializable(typeof(VslEngineProvenance))]
internal sealed partial class VslJsonContext : JsonSerializerContext;