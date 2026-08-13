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
/// <remarks>
/// <para>
/// Les archives client officielles portent TOUTES un dossier racine, constaté le 2026-08-13 sur la
/// 1.22.6 en lisant les premières entrées par requête de plage HTTP (voir
/// <c>GameArchiveLayoutLiveTests</c>) : <c>vintagestory/</c> pour <c>linux-x64</c>,
/// <c>Vintage Story.app/</c> pour <c>osx-x64</c> et <c>osx-arm64</c>. Extraites telles quelles,
/// elles posaient le jeu dans <c>versions/&lt;version&gt;/vintagestory/</c> alors que le lancement
/// et la vérification post-installation l'attendent à la RACINE du dossier de version : aucune
/// installation Linux n'a jamais abouti. Les fixtures de test, elles, modélisaient un tar SANS
/// dossier racine, ce qui rendait le défaut invisible.
/// </para>
/// <para>
/// D'où la règle d'aplatissement, équivalente à <c>tar --strip-components=1</c> mais CONDITIONNELLE
/// et fondée sur ce que l'archive contient vraiment : quand tout ce qui a été écrit tient sous un
/// unique dossier de premier niveau, ce dossier est aplati ; sinon l'extraction reste telle quelle.
/// Une archive à deux racines, ou dont la seule entrée est un fichier, n'est donc pas touchée.
/// </para>
/// </remarks>
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

    public async Task InstallAsync(
        string archivePath,
        string targetDirectory,
        IProgress<GameInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var root = _fileSystem.Path.GetFullPath(targetDirectory);
        _fileSystem.Directory.CreateDirectory(root);

        ExtractedLayout layout;
        try
        {
            layout = await ExtractAsync(archivePath, root, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw GameInstallFailedException.ForArchive(archivePath, exception);
        }

        // L'extraction est finie, la pose des bits d'exécution commence : elle parcourt tout ce qui
        // vient d'être écrit et n'est pas instantanée sur une installation complète. La barre reste
        // donc pleine plutôt que de retomber à l'indéterminé, ce qui se lirait comme un incident.
        progress?.Report(GameInstallProgress.ForInstalling(1d));

        // L'aplatissement passe AVANT les bits d'exécution : autrement le dossier de transit se
        // ferait chmodder pour rien, et surtout l'arborescence finale ne serait pas encore celle
        // que l'on veut rendre exécutable.
        FlattenSingleRootFolder(root, layout);
        ApplyExecutableBits(root);
    }

    /// <remarks>
    /// Le tar est lu EN FLUX : le nombre d'entrées n'est pas connu d'avance, et le compter
    /// demanderait une première passe complète sur plusieurs centaines de mégaoctets. La position
    /// dans l'archive compressée est le seul repère disponible sans ce coût — monotone, bornée par
    /// la taille du fichier, et un peu en avance sur ce qui est réellement écrit puisque
    /// <see cref="GZipStream"/> lit par blocs. C'est un repère de progression, pas une mesure
    /// d'octets posés sur le disque, et c'est pourquoi seul son RAPPORT est publié.
    /// </remarks>
    private async Task<ExtractedLayout> ExtractAsync(string archivePath, string root, IProgress<GameInstallProgress>? progress, CancellationToken cancellationToken)
    {
        var layout = new ExtractedLayout();

        var archive = _fileSystem.File.OpenRead(archivePath);
        await using (archive.ConfigureAwait(false))
        {
            var total = archive.CanSeek ? archive.Length : 0L;
            var lastPercent = -1;

            var decompressed = new GZipStream(archive, CompressionMode.Decompress);
            await using (decompressed.ConfigureAwait(false))
            {
                var reader = new TarReader(decompressed, leaveOpen: true);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
                    {
                        await WriteEntryAsync(entry, root, layout, cancellationToken).ConfigureAwait(false);

                        if (progress is null || total <= 0)
                        {
                            continue;
                        }

                        // Un rapport par point de pourcentage : une archive du jeu contient des
                        // dizaines de milliers d'entrées, et chaque consommateur repasse par le
                        // dispatcher de l'interface. Cent messages suffisent à remplir une barre.
                        var ratio = Math.Clamp((double)archive.Position / total, 0d, 1d);
                        var percent = (int)(ratio * 100d);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress.Report(GameInstallProgress.ForInstalling(ratio));
                        }
                    }
                }
            }
        }

        return layout;
    }

    private async Task WriteEntryAsync(TarEntry entry, string root, ExtractedLayout layout, CancellationToken cancellationToken)
    {
        var destination = ResolveSafePath(root, entry.Name);
        if (destination is null)
        {
            // Entrée dont le chemin sortirait du dossier cible : on ne l'écrit pas. Une archive
            // est un fichier distant, elle n'a pas à décider où on écrit. Elle n'entre pas non plus
            // dans le relevé de topologie : ce qui décide de l'aplatissement, c'est ce qui a été
            // ÉCRIT, pas ce que l'archive prétendait contenir.
            return;
        }

        switch (entry.EntryType)
        {
            case TarEntryType.Directory:
                _fileSystem.Directory.CreateDirectory(destination);
                layout.Record(RelativeSegments(root, destination));
                break;

            case TarEntryType.RegularFile:
            case TarEntryType.V7RegularFile:
                await WriteFileAsync(entry, destination, cancellationToken).ConfigureAwait(false);
                layout.Record(RelativeSegments(root, destination));
                break;

            default:
                // Liens symboliques, liens durs, périphériques, métadonnées étendues : les builds
                // du jeu n'en contiennent pas et les recréer fidèlement demanderait des privilèges
                // ou des cas particuliers par OS. On les ignore explicitement.
                break;
        }
    }

    /// <summary>
    /// Segments du chemin écrit, relatifs à la cible. Dérivés du chemin RÉSOLU et non du nom brut
    /// de l'entrée : un tar peut préfixer ses noms de <c>./</c> ou multiplier les séparateurs, et
    /// c'est l'emplacement réellement écrit qui décide de la topologie.
    /// </summary>
    private string[] RelativeSegments(string root, string destination)
        => _fileSystem.Path.GetRelativePath(root, destination)
            .Split([_fileSystem.Path.DirectorySeparatorChar, _fileSystem.Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Remonte d'un cran le contenu du dossier racine unique, quand il y en a un. Sans effet sinon.
    /// </summary>
    /// <remarks>
    /// Le dossier est d'abord renommé en un nom de transit : un enfant qui porte le nom de son
    /// propre parent (<c>vintagestory/vintagestory</c>, exactement le cas de l'archive Linux, dont
    /// le binaire s'appelle <c>Vintagestory</c>) entrerait sinon en collision avec lui au moment
    /// d'être déplacé. Les déplacements se font tous à l'intérieur de la cible, donc le durcissement
    /// de <see cref="ResolveSafePath"/> reste entier : rien ne peut sortir du dossier de version.
    /// </remarks>
    private void FlattenSingleRootFolder(string root, ExtractedLayout layout)
    {
        if (layout.SingleRootFolder is not { } folder)
        {
            return;
        }

        var source = _fileSystem.Path.Combine(root, folder);
        if (!_fileSystem.Directory.Exists(source))
        {
            return;
        }

        var staging = _fileSystem.Path.Combine(root, $".prospect-flatten-{Guid.NewGuid():N}");
        _fileSystem.Directory.Move(source, staging);

        foreach (var directory in _fileSystem.Directory.GetDirectories(staging))
        {
            _fileSystem.Directory.Move(directory, _fileSystem.Path.Combine(root, _fileSystem.Path.GetFileName(directory)));
        }

        foreach (var file in _fileSystem.Directory.GetFiles(staging))
        {
            _fileSystem.File.Move(file, _fileSystem.Path.Combine(root, _fileSystem.Path.GetFileName(file)));
        }

        _fileSystem.Directory.Delete(staging, recursive: true);
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

    /// <summary>
    /// Ce que l'extraction a réellement posé sous la cible, réduit à la seule question qui décide
    /// de l'aplatissement : combien de noms de premier niveau, et l'un d'eux contient-il quelque
    /// chose. Relevé au fil du flux, parce que l'archive n'est lue qu'une fois : la compter en
    /// première passe coûterait une décompression complète de 600 Mo.
    /// </summary>
    private sealed class ExtractedLayout
    {
        private readonly HashSet<string> _topLevelNames = new(StringComparer.Ordinal);
        private bool _sawNestedEntry;

        /// <summary>
        /// Nom du dossier racine à aplatir, ou <see langword="null"/> quand la règle ne s'applique
        /// pas : plusieurs noms de premier niveau, ou un seul mais qui ne contient rien (une archive
        /// réduite à un fichier unique n'a pas de dossier racine à retirer).
        /// </summary>
        public string? SingleRootFolder
            => _sawNestedEntry && _topLevelNames.Count == 1 ? _topLevelNames.First() : null;

        /// <summary>Relève un chemin écrit, exprimé en segments relatifs à la cible.</summary>
        public void Record(string[] segments)
        {
            if (segments.Length == 0)
            {
                return;
            }

            _topLevelNames.Add(segments[0]);
            _sawNestedEntry |= segments.Length > 1;
        }
    }
}