using System.Text.Json.Serialization;

namespace Prospect.Core.ModDb;

/// <summary>
/// Contexte source-gen du domaine ModDb (docs/architecture.md : System.Text.Json avec générateurs
/// de source, jamais de réflexion). Couvre les enveloppes v1, les documents v2 et le document de
/// cache disque.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ModDbModListResponseDto))]
[JsonSerializable(typeof(ModDbModDetailResponseDto))]
[JsonSerializable(typeof(ModDbTagListResponseDto))]
[JsonSerializable(typeof(ModDbV2ReleaseDto))]
[JsonSerializable(typeof(ModDbV2InstallInformationResponseDto))]
[JsonSerializable(typeof(ModDbCacheDocument))]
internal sealed partial class ModDbJsonContext : JsonSerializerContext;