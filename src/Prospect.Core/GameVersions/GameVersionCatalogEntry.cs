using Prospect.Core.Common;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Une version du jeu telle que la publie le catalogue, avec ses fichiers par plateforme. Le
/// canal n'a pas de champ dédié dans le document distant : il se déduit du nom de version, via
/// <see cref="Common.GameVersion.Channel"/>.
/// </summary>
/// <param name="Version">Version du jeu, clé de premier niveau du document.</param>
/// <param name="Assets">Fichiers par clé de plateforme (voir <see cref="GamePlatforms"/>).</param>
public sealed record GameVersionCatalogEntry(GameVersion Version, IReadOnlyDictionary<string, GameVersionAsset> Assets)
{
    /// <summary>Canal déduit du suffixe de version.</summary>
    public GameVersionChannel Channel => Version.Channel;

    /// <summary>
    /// Premier fichier disponible parmi <paramref name="platformKeys"/>, dans l'ordre donné, ou
    /// <see langword="null"/> si le catalogue n'en propose aucun. C'est ce qui traduit la
    /// préférence <c>mac-arm64</c> puis repli <c>mac-x64</c> sans que la stratégie d'installation
    /// ait à connaître la structure du catalogue.
    /// </summary>
    public GameVersionAsset? FindAsset(IReadOnlyList<string> platformKeys)
    {
        ArgumentNullException.ThrowIfNull(platformKeys);

        foreach (var key in platformKeys)
        {
            if (Assets.TryGetValue(key, out var asset))
            {
                return asset;
            }
        }

        return null;
    }
}