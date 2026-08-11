using System.Text.Json.Serialization;

namespace Prospect.Core.Modpacks;

/// <summary>
/// Contexte source-gen du domaine Modpacks : fournit le
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/> qui lit et écrit
/// <c>prospect-pack.json</c>, en camelCase comme documenté dans docs/architecture.md et indenté
/// pour rester lisible dans un manifest destiné à voyager et parfois être inspecté à la main.
/// Aucune réflexion : <see cref="Common.GameVersion"/> et <see cref="Common.ModVersion"/> (via
/// leurs converters dédiés) sont couverts automatiquement en tant que types référencés par
/// <see cref="ModpackManifest"/>. Les champs JSON inconnus sont ignorés par défaut par
/// <see cref="System.Text.Json"/> (<c>UnmappedMemberHandling.Skip</c>) : un schéma futur qui
/// ajoute des champs reste lisible par cette version de Prospect.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ModpackManifest))]
public sealed partial class ModpackJsonContext : JsonSerializerContext;