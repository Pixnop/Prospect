using System.Text.Json.Serialization;

namespace Prospect.Core.Settings;

/// <summary>
/// Contexte source-gen du domaine Settings : fournit le
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/> qu'exige
/// <see cref="Storage.JsonFileStore"/> pour lire et écrire <c>prospect.json</c>, en camelCase comme
/// <see cref="Instances.InstanceJsonContext"/>. <see cref="ThemePreference"/> et
/// <see cref="DownloadPreferences"/> sont couverts automatiquement en tant que types référencés par
/// <see cref="ProspectSettings"/>. <c>UseStringEnumConverter</c> écrit <see cref="ThemePreference"/>
/// en toutes lettres (<c>"Dark"</c>, pas <c>0</c>) : <c>prospect.json</c> est un fichier que
/// l'utilisateur peut ouvrir à la main (docs/architecture.md, « le système de fichiers est la
/// source de vérité »), une valeur numérique opaque n'y aurait pas sa place.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProspectSettings))]
public sealed partial class ProspectSettingsJsonContext : JsonSerializerContext;