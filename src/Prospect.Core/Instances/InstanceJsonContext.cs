using System.Text.Json.Serialization;

namespace Prospect.Core.Instances;

/// <summary>
/// Contexte source-gen du domaine Instances : fournit le <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/>
/// qu'exige <see cref="Prospect.Core.Storage.JsonFileStore"/> pour lire et écrire
/// <c>instance.json</c>, en camelCase comme documenté dans docs/architecture.md. Aucune
/// réflexion : <see cref="InstanceLaunchSettings"/> et <see cref="Common.GameVersion"/> (via son
/// propre converter) sont couverts automatiquement en tant que types référencés par
/// <see cref="InstanceMetadata"/>.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InstanceMetadata))]
public sealed partial class InstanceJsonContext : JsonSerializerContext;