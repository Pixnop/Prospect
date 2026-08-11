using System.Text.Json.Serialization;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Contexte source-gen du domaine GameVersions (docs/architecture.md : System.Text.Json avec
/// générateurs de source, jamais de réflexion). Couvre le document distant
/// (<c>{ version: { plateforme: {...} } }</c>) et le document de cache disque.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, GameCatalogPlatformDto>>))]
[JsonSerializable(typeof(GameCatalogCacheDocument))]
internal sealed partial class GameVersionsJsonContext : JsonSerializerContext;