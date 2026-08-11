using System.Text.Json;

using Prospect.Core.Common;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Conversion des deux documents distants en modèle du domaine. Pur calcul, sans effet de bord :
/// c'est la partie du catalogue que les tests nourrissent des échantillons réels relevés par la
/// recherche.
/// </summary>
internal static class GameCatalogParser
{
    /// <summary>
    /// Fusionne <c>stable.json</c> et <c>unstable.json</c>, puis trie par version décroissante.
    /// L'ordre de fusion reprend celui de VS Launcher (<c>{ ...unstable, ...stable }</c>) : à clé
    /// égale, l'entrée stable l'emporte.
    /// </summary>
    /// <exception cref="JsonException">L'un des documents n'est pas un JSON exploitable.</exception>
    public static IReadOnlyList<GameVersionCatalogEntry> Merge(string stableJson, string unstableJson)
    {
        var merged = new Dictionary<string, Dictionary<string, GameCatalogPlatformDto>>(StringComparer.Ordinal);

        foreach (var document in new[] { unstableJson, stableJson })
        {
            foreach (var (version, platforms) in Deserialize(document))
            {
                merged[version] = platforms;
            }
        }

        return merged
            .Select(pair => ConvertEntry(pair.Key, pair.Value))
            .OfType<GameVersionCatalogEntry>()
            .OrderByDescending(entry => entry.Version)
            .ToArray();
    }

    private static Dictionary<string, Dictionary<string, GameCatalogPlatformDto>> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize(json, GameVersionsJsonContext.Default.DictionaryStringDictionaryStringGameCatalogPlatformDto) ?? [];
    }

    // Une version dont le nom ne respecte pas notre grammaire, ou qui n'offre aucun fichier
    // exploitable, est ignorée plutôt que de faire échouer tout le catalogue : le document est
    // distant et public, il n'a aucune obligation envers nous.
    private static GameVersionCatalogEntry? ConvertEntry(string versionKey, Dictionary<string, GameCatalogPlatformDto> platforms)
    {
        if (!GameVersion.TryParse(versionKey, out var version))
        {
            return null;
        }

        var assets = new Dictionary<string, GameVersionAsset>(StringComparer.Ordinal);
        foreach (var (platformKey, dto) in platforms)
        {
            var asset = ConvertAsset(platformKey, dto);
            if (asset is not null)
            {
                assets[platformKey] = asset;
            }
        }

        return assets.Count == 0 ? null : new GameVersionCatalogEntry(version, assets);
    }

    private static GameVersionAsset? ConvertAsset(string platformKey, GameCatalogPlatformDto? dto)
    {
        if (dto is null
            || string.IsNullOrWhiteSpace(dto.FileName)
            || string.IsNullOrWhiteSpace(dto.Md5)
            || !TryParseAbsoluteUri(dto.Urls?.Cdn, out var cdn)
            || !TryParseAbsoluteUri(dto.Urls?.Local, out var local))
        {
            return null;
        }

        return new GameVersionAsset(
            platformKey,
            dto.FileName,
            string.IsNullOrWhiteSpace(dto.FileSize) ? "taille inconnue" : dto.FileSize,
            dto.Md5,
            cdn,
            local,
            dto.Latest == 1);
    }

    private static bool TryParseAbsoluteUri(string? value, out Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }
}