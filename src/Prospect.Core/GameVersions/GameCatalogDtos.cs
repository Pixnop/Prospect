using System.Text.Json.Serialization;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Les deux miroirs d'un fichier. DTO défensif : les deux champs sont nullables, une entrée
/// amputée est ignorée à la conversion plutôt que de faire échouer tout le document.
/// </summary>
internal sealed record GameCatalogUrlsDto
{
    [JsonPropertyName("cdn")]
    public string? Cdn { get; init; }

    [JsonPropertyName("local")]
    public string? Local { get; init; }
}

/// <summary>
/// Une entrée plateforme du catalogue, calquée sur l'échantillon réel relevé par la recherche
/// (docs/research/vslauncher-et-distribution.md, section b). Tous les champs sont facultatifs
/// côté DTO : le contrat de cette API publique n'est écrit nulle part, il est simplement observé,
/// donc tout champ peut manquer sans préavis. La validation (« que faut-il au minimum pour
/// pouvoir télécharger ? ») a lieu à la conversion, dans <see cref="GameCatalogParser"/>.
/// </summary>
internal sealed record GameCatalogPlatformDto
{
    [JsonPropertyName("filename")]
    public string? FileName { get; init; }

    /// <summary>Chaîne lisible du type « 590.5 MB », pas un nombre d'octets.</summary>
    [JsonPropertyName("filesize")]
    public string? FileSize { get; init; }

    [JsonPropertyName("md5")]
    public string? Md5 { get; init; }

    [JsonPropertyName("urls")]
    public GameCatalogUrlsDto? Urls { get; init; }

    /// <summary>Drapeau 0/1 du catalogue, absent sur les versions anciennes.</summary>
    [JsonPropertyName("latest")]
    public int Latest { get; init; }
}