namespace Prospect.Core.Migration;

/// <summary>
/// Un moteur (« Game Version » au sens de VS Launcher) tel que lu depuis <c>config.json</c> :
/// <c>GameVersionType</c> de VSL (<c>global.d.ts</c>), une simple paire version/chemin, bien plus
/// simple que <see cref="VslInstallation"/>. Comme elle, <see cref="Path"/> est le chemin réel
/// choisi par l'utilisateur, pas une reconstruction depuis <c>VSLGameVersions/&lt;version&gt;</c>.
/// </summary>
public sealed record VslGameVersionEntry
{
    /// <summary>Version du jeu, chaîne brute non validée (voir <see cref="Common.GameVersion.TryParse"/> côté adoption).</summary>
    public required string Version { get; init; }

    /// <summary>Dossier contenant les fichiers du moteur (exécutable, assets).</summary>
    public required string Path { get; init; }
}