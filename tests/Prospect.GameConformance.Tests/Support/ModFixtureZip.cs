#if PROSPECT_CONFORMANCE_ENGINE
using System.IO.Compression;

using Prospect.Core.ModDb;

namespace Prospect.GameConformance.Tests.Support;

/// <summary>
/// Construit, sur le vrai système de fichiers, un zip de mod minimal valide : un seul
/// <c>modinfo.json</c> (<see cref="ModInfoParser.FileName"/>, jamais codé en dur) à la racine de
/// l'archive. Aucun asset n'est nécessaire pour qu'un mod de type <c>content</c> soit reconnu par
/// le moteur — c'est même une partie de ce que les tests de conformité vérifient.
/// </summary>
internal static class ModFixtureZip
{
    /// <summary>
    /// Écrit un zip contenant <paramref name="modInfoJson"/> sous <see cref="ModInfoParser.FileName"/>,
    /// au chemin <paramref name="destinationPath"/>. Le répertoire parent est créé s'il n'existe pas ;
    /// tout fichier déjà présent dans ce répertoire est supprimé au préalable, pour ne jamais faire
    /// cohabiter deux archives dans le même dossier <c>Mods/</c> seedé (utile quand le nom du fichier
    /// désactivé change d'une exécution à l'autre si la convention change).
    /// </summary>
    public static void WriteSingleFixture(string destinationPath, string modInfoJson)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException($"Chemin de destination sans répertoire parent : '{destinationPath}'.", nameof(destinationPath));

        Directory.CreateDirectory(directory);
        foreach (var existing in Directory.EnumerateFiles(directory))
        {
            File.Delete(existing);
        }

        WriteZip(destinationPath, modInfoJson);
    }

    /// <summary>
    /// Écrit un zip contenant <paramref name="modInfoJson"/> dans <paramref name="directory"/>, en
    /// conservant tout autre fichier déjà présent (permet de seeder plusieurs mods indépendants
    /// dans le même dossier <c>Mods/</c> pour un seul boot du serveur — voir
    /// <c>ModInfoParsingAgreementTests</c>).
    /// </summary>
    public static void AddFixture(string directory, string fileName, string modInfoJson)
    {
        Directory.CreateDirectory(directory);
        WriteZip(Path.Combine(directory, fileName), modInfoJson);
    }

    private static void WriteZip(string destinationPath, string modInfoJson)
    {
        using var stream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(ModInfoParser.FileName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(modInfoJson);
    }
}
#endif