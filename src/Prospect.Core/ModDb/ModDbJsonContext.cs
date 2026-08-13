using System.Text.Json.Serialization;

namespace Prospect.Core.ModDb;

/// <summary>
/// Contexte source-gen du domaine ModDb (docs/architecture.md : System.Text.Json avec générateurs
/// de source, jamais de réflexion). Couvre les enveloppes v1, les documents v2 et le document de
/// cache disque.
/// </summary>
/// <remarks>
/// Les types de VALEUR des maps du ModDB y sont déclarés explicitement, en plus des enveloppes qui
/// les contiennent : <see cref="PhpAssociativeArrayConverter{TValue}"/> résout son
/// <c>JsonTypeInfo</c> par le résolveur des options, et compter sur la seule accessibilité
/// transitive rendrait ce contrat implicite.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ModDbModListResponseDto))]
[JsonSerializable(typeof(ModDbModDetailResponseDto))]
[JsonSerializable(typeof(ModDbUpdatesResponseDto))]
[JsonSerializable(typeof(ModDbTagListResponseDto))]
[JsonSerializable(typeof(ModDbV2ReleaseDto))]
[JsonSerializable(typeof(ModDbV2InstallInformationResponseDto))]
[JsonSerializable(typeof(ModDbCacheDocument))]
[JsonSerializable(typeof(ModDbReleaseDto))]
[JsonSerializable(typeof(ModDbV2InstallInformationEntryDto))]
internal sealed partial class ModDbJsonContext : JsonSerializerContext;