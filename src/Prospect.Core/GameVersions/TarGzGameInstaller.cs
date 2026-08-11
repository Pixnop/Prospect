using System.Formats.Tar;
using System.IO.Abstractions;
using System.IO.Compression;

using Prospect.Core.Common;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Extraction d'une archive <c>.tar.gz</c> du jeu puis restauration des bits d'exécution. Écrit
/// avec <see cref="TarReader"/> et <see cref="GZipStream"/> de la BCL plutôt qu'avec un binaire
/// 7-Zip embarqué comme le faisait VS Launcher : une dépendance native de moins, et le flux passe
/// par <see cref="IFileSystem"/>, donc tout est testable sans toucher au disque.
/// </summary>
internal sealed class TarGzGameInstaller
{
    /// <summary>
    /// <c>rwxr-xr-x</c>. Appliqué à tout le contenu extrait, comme le faisait VS Launcher : le tar
    /// ne restitue pas les bits d'exécution à travers notre extraction, et sans eux le binaire
    /// natif <c>Vintagestory</c> refuse de démarrer.
    /// </summary>
    private const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly IFileSystem _fileSystem;
    private readonly IUnixFilePermissions _permissions;

    public TarGzGameInstaller(IFileSystem fileSystem, IUnixFilePermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(permissions);

        _fileSystem = fileSystem;
        _permissions = permissions;
    }

    public async Task InstallAsync(string archivePath, string targetDirectory, CancellationToken cancellationToken)
    {
        var root = _fileSystem.Path.GetFullPath(targetDirectory);
        _fileSystem.Directory.CreateDirectory(root);

        try
        {
            await ExtractAsync(archivePath, root, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw GameInstallFailedException.ForArchive(archivePath, exception);
        }

        ApplyExecutableBits(root);
    }

    private async Task ExtractAsync(string archivePath, string root, CancellationToken cancellationToken)
    {
        var archive = _fileSystem.File.OpenRead(archivePath);
        await using (archive.ConfigureAwait(false))
        {
            var decompressed = new GZipStream(archive, CompressionMode.Decompress);
            await using (decompressed.ConfigureAwait(false))
            {
                var reader = new TarReader(decompressed, leaveOpen: true);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
                    {
                        await WriteEntryAsync(entry, root, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private async Task WriteEntryAsync(TarEntry entry, string root, CancellationToken cancellationToken)
    {
        var destination = ResolveSafePath(root, entry.Name);
        if (destination is null)
        {
            // Entrée dont le chemin sortirait du dossier cible : on ne l'écrit pas. Une archive
            // est un fichier distant, elle n'a pas à décider où on écrit.
            return;
        }

        switch (entry.EntryType)
        {
            case TarEntryType.Directory:
                _fileSystem.Directory.CreateDirectory(destination);
                break;

            case TarEntryType.RegularFile:
            case TarEntryType.V7RegularFile:
                await WriteFileAsync(entry, destination, cancellationToken).ConfigureAwait(false);
                break;

            default:
                // Liens symboliques, liens durs, périphériques, métadonnées étendues : les builds
                // du jeu n'en contiennent pas et les recréer fidèlement demanderait des privilèges
                // ou des cas particuliers par OS. On les ignore explicitement.
                break;
        }
    }

    private async Task WriteFileAsync(TarEntry entry, string destination, CancellationToken cancellationToken)
    {
        var parent = _fileSystem.Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            _fileSystem.Directory.CreateDirectory(parent);
        }

        if (entry.DataStream is not { } data)
        {
            return;
        }

        var file = _fileSystem.File.Create(destination);
        await using (file.ConfigureAwait(false))
        {
            await data.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ApplyExecutableBits(string root)
    {
        _permissions.SetMode(root, ExecutableMode);

        foreach (var directory in _fileSystem.Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
        {
            _permissions.SetMode(directory, ExecutableMode);
        }

        foreach (var file in _fileSystem.Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            _permissions.SetMode(file, ExecutableMode);
        }
    }

    // Garde-fou contre les archives dont une entrée remonte hors de la cible (« zip slip »).
    private string? ResolveSafePath(string root, string entryName)
    {
        var relative = entryName.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        var candidate = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(root, relative));
        var prefix = root.EndsWith(_fileSystem.Path.DirectorySeparatorChar) ? root : root + _fileSystem.Path.DirectorySeparatorChar;

        return candidate.StartsWith(prefix, StringComparison.Ordinal) ? candidate : null;
    }
}