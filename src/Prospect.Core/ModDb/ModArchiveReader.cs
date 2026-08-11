using System.IO.Abstractions;
using System.IO.Compression;
using System.Text;

namespace Prospect.Core.ModDb;

/// <summary>Ce qu'une archive de mod a livré : ses métadonnées, et son icône si elle en porte une.</summary>
/// <param name="Result">Métadonnées lues, ou raison de l'échec.</param>
/// <param name="Icon">Contenu de l'icône, ou <see langword="null"/> si l'archive n'en contient pas.</param>
public sealed record ModArchiveContent(ModInfoResult Result, byte[]? Icon);

/// <summary>
/// Ouvre un zip de mod et en extrait <c>modinfo.json</c> et l'icône. Passe par
/// <see cref="IFileSystem"/> pour l'accès disque et par <see cref="ZipArchive"/> pour le contenu :
/// les tests construisent de vraies archives en mémoire, il n'y a donc rien à simuler côté format.
/// </summary>
/// <remarks>
/// Aucune archive n'est jamais extraite sur disque et aucune entrée n'est écrite : seules deux
/// entrées connues sont lues en flux. C'est ce qui met ce composant hors de portée des attaques de
/// traversée de chemin par nom d'entrée, un classique des archives venues d'internet.
/// </remarks>
public sealed class ModArchiveReader
{
    /// <summary>Taille au-delà de laquelle une icône est ignorée : au-dessus, ce n'est plus une icône.</summary>
    public const int MaxIconBytes = 2 * 1024 * 1024;

    private readonly IFileSystem _fileSystem;

    /// <summary>Construit le lecteur.</summary>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    public ModArchiveReader(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Lit l'archive <paramref name="archivePath"/>.
    /// </summary>
    /// <param name="archivePath">Chemin du zip.</param>
    /// <returns>
    /// Toujours un résultat, jamais une exception : un fichier absent, tronqué ou qui n'est pas un
    /// zip donne un <see cref="ModInfoProblem.UnreadableArchive"/>, ce que la liste des mods
    /// affiche comme « non identifié » avec sa raison.
    /// </returns>
    public ModArchiveContent Read(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        if (!_fileSystem.File.Exists(archivePath))
        {
            return new ModArchiveContent(ModInfoResult.Failure(ModInfoProblem.UnreadableArchive), null);
        }

        try
        {
            using var stream = _fileSystem.File.OpenRead(archivePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var entry = FindEntry(archive, ModInfoParser.FileName);
            if (entry is null)
            {
                return new ModArchiveContent(ModInfoResult.Failure(ModInfoProblem.MissingModInfo), ReadIcon(archive, null));
            }

            var result = ModInfoParser.Parse(ReadAllText(entry));

            return new ModArchiveContent(result, ReadIcon(archive, result.Info?.IconPath));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new ModArchiveContent(ModInfoResult.Failure(ModInfoProblem.UnreadableArchive), null);
        }
    }

    // Le fichier est cherché d'abord à la racine, où la convention le place, puis n'importe où
    // dans l'archive : quelques mods sont zippés avec un dossier parent superflu.
    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string fileName)
        => archive.Entries.FirstOrDefault(candidate => string.Equals(candidate.FullName, fileName, StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(candidate => string.Equals(candidate.Name, fileName, StringComparison.OrdinalIgnoreCase));

    private static byte[]? ReadIcon(ZipArchive archive, string? declaredPath)
    {
        var entry = declaredPath is null ? null : FindEntry(archive, NormalizeIconPath(declaredPath));
        entry ??= FindEntry(archive, ModInfoParser.DefaultIconFileName);

        if (entry is null || entry.Length > MaxIconBytes)
        {
            return null;
        }

        try
        {
            using var source = entry.Open();
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);

            return buffer.ToArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // Une icône illisible n'a jamais empêché un mod de fonctionner : la liste affichera
            // simplement l'icône générique.
            return null;
        }
    }

    // iconPath s'écrit « ./modicon.png » dans les échantillons : les entrées de zip, elles, ne
    // portent jamais ce préfixe.
    private static string NormalizeIconPath(string declaredPath)
    {
        var normalized = declaredPath.Replace('\\', '/').TrimStart();

        return normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized;
    }

    private static string ReadAllText(ZipArchiveEntry entry)
    {
        using var source = entry.Open();
        // detectEncodingFromByteOrderMarks : quelques modinfo.json sont enregistrés en UTF-8 avec
        // BOM, que le jeu accepte et que StreamReader doit donc absorber sans le rendre comme un
        // caractère parasite en tête de fichier.
        using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        return reader.ReadToEnd();
    }
}