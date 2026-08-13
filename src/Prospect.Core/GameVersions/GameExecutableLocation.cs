using System.IO.Abstractions;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Emplacement d'un exécutable du jeu à l'intérieur d'un dossier d'installation, exprimé en
/// SEGMENTS de chemin et jamais en chaîne toute faite.
/// </summary>
/// <remarks>
/// Le modèle est le même sur les trois OS parce qu'aucun séparateur n'y est écrit : c'est le
/// <see cref="IFileSystem"/> injecté qui assemble, donc <c>versions\1.22.6\Vintagestory.exe</c> sur
/// Windows et <c>versions/1.22.6/Vintagestory</c> ailleurs, sans que le domaine ait à le savoir.
/// Les défauts Windows des chantiers précédents venaient tous d'un séparateur en dur.
/// </remarks>
/// <param name="Segments">Segments relatifs au dossier d'installation, du plus haut au plus bas.</param>
public sealed record GameExecutableLocation(IReadOnlyList<string> Segments)
{
    /// <summary>Construit un emplacement depuis ses segments.</summary>
    public static GameExecutableLocation Of(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return new GameExecutableLocation(segments);
    }

    /// <summary>Chemin absolu de cet exécutable sous <paramref name="installDirectory"/>.</summary>
    public string ResolveIn(IFileSystem fileSystem, string installDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrEmpty(installDirectory);

        return fileSystem.Path.Combine([installDirectory, .. Segments]);
    }

    /// <summary>
    /// Forme lisible pour un message ou un journal. Toujours en barres obliques, parce que c'est une
    /// DESCRIPTION et non un chemin à ouvrir : le chemin réel, lui, sort de <see cref="ResolveIn"/>.
    /// </summary>
    public override string ToString() => string.Join('/', Segments);
}