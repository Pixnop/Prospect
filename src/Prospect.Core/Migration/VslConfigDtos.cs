using System.Text.Json.Serialization;

namespace Prospect.Core.Migration;

/// <summary>
/// Forme JSON brute d'une entrée <c>installations[]</c> de <c>config.json</c>
/// (<c>InstallationType</c>, <c>global.d.ts</c>). Tous les champs sont facultatifs côté DTO, à
/// l'image de <c>ensureConfigProperties</c> côté VSL lui-même (chaque champ manquant retombe sur un
/// défaut plutôt que de faire échouer le document) : la validation (« que faut-il au minimum pour
/// pouvoir adopter ? ») a lieu à la conversion, dans <see cref="VslConfigParser"/>.
/// </summary>
internal sealed record VslInstallationDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("startParams")]
    public string? StartParams { get; init; }

    [JsonPropertyName("envVars")]
    public string? EnvVars { get; init; }

    [JsonPropertyName("mesaGlThread")]
    public bool MesaGlThread { get; init; }

    /// <summary>
    /// Nullable plutôt qu'un <c>long</c> avec initialiseur : le générateur de source
    /// <c>System.Text.Json</c> n'applique pas l'initialiseur de propriété d'un champ absent du
    /// JSON pour ce type de DTO (vérifié empiriquement), donc le défaut -1 de
    /// <c>defaultInstallation.lastTimePlayed</c> côté VSL (jamais joué) est appliqué explicitement
    /// à la conversion, dans <see cref="VslConfigParser"/>, plutôt que sur ce champ.
    /// </summary>
    [JsonPropertyName("lastTimePlayed")]
    public long? LastTimePlayed { get; init; }

    [JsonPropertyName("totalTimePlayed")]
    public long? TotalTimePlayed { get; init; }
}

/// <summary>Forme JSON brute d'une entrée <c>gameVersions[]</c> de <c>config.json</c> (<c>GameVersionType</c>, <c>global.d.ts</c>).</summary>
internal sealed record VslGameVersionDto
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }
}