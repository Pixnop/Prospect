using System.Globalization;
using System.IO.Abstractions;
using System.IO.Compression;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Storage;

namespace Prospect.Core.Backups;

/// <summary>
/// Sauvegardes d'instance (docs/research/vslauncher-et-distribution.md section « Sauvegardes »,
/// docs/architecture.md « Après le MVP ») : créer, lister, supprimer et restaurer une archive de
/// <c>data/</c> entier (mondes, configs, mods), stockée à côté de <c>data/</c> plutôt que dedans.
/// </summary>
/// <remarks>
/// <para>
/// <b>Emplacement.</b> <c>instances/&lt;slug&gt;/backups/&lt;horodatage&gt;.zip</c>, un sous-dossier
/// SIBLING de <c>data/</c> (jamais à l'intérieur) : c'est ce placement, et lui seul, qui garantit
/// qu'une duplication d'instance (<see cref="InstanceService.DuplicateAsync"/>, qui ne copie que
/// <c>data/</c>) ne traîne jamais les sauvegardes de la source avec elle. Le nom du dossier
/// (<c>backups</c>) n'est connu que de cette classe, jamais exposé par <see cref="IInstanceRepository"/> :
/// même principe que <c>DataDirectoryName</c>/<c>SavesDirectoryName</c> privés à
/// <see cref="FileSystemInstanceRepository"/>, ce service-ci est le seul point du Core à connaître
/// la topologie des sauvegardes.
/// </para>
/// <para>
/// <b>Compression.</b> Niveau fixe (<see cref="CompressionLevel.Optimal"/>), jamais configurable
/// par instance : VS Launcher expose un <c>compressionLevel</c> par installation
/// (docs/research/vslauncher-et-distribution.md section f), mais un joueur qui veut juste que son
/// monde soit protégé n'a rien à choisir entre « plus rapide » et « plus petit ». Voir aussi
/// <see cref="Instances.InstanceBackupSettings"/>, qui documente cette même décision côté modèle.
/// </para>
/// <para>
/// <b>Restauration sûre.</b> <see cref="RestoreAsync"/> ne modifie <c>data/</c> qu'à la toute
/// dernière étape, et seulement par un renommage de dossier (opération quasi instantanée), jamais
/// par une suppression suivie d'une recopie fichier par fichier : voir la docstring de
/// <see cref="RestoreAsync"/> pour l'ordre exact et pourquoi il protège contre un échec en cours de
/// route.
/// </para>
/// </remarks>
public sealed class InstanceBackupService
{
    private const string BackupsDirectoryName = "backups";
    private const string ZipExtension = ".zip";
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    // Suffixes des dossiers de travail utilisés pendant une restauration (voir RestoreAsync) :
    // jamais un nom qu'un joueur choisirait pour une instance, pour qu'un résidu d'une tentative
    // interrompue ne puisse jamais être confondu avec un vrai dossier de données.
    private const string StagingDirectorySuffix = ".prospect-restore-staging";
    private const string AsideDirectorySuffix = ".prospect-restore-aside";

    private readonly IInstanceRepository _instances;
    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;

    public InstanceBackupService(IInstanceRepository instances, IFileSystem fileSystem, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(clock);

        _instances = instances;
        _fileSystem = fileSystem;
        _clock = clock;
    }

    /// <summary>Dossier des sauvegardes de cette instance (<c>instances/&lt;slug&gt;/backups</c>), qu'il existe ou non.</summary>
    public string GetBackupsDirectory(string slug)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);

        return _fileSystem.Path.Combine(_instances.GetInstanceDirectory(slug), BackupsDirectoryName);
    }

    /// <summary>
    /// Zippe tout <c>data/</c> (mondes, configs, mods : la restauration doit rendre l'instance
    /// exactement rejouable) vers <c>backups/&lt;horodatage-IClock&gt;.zip</c>, avec progression et
    /// annulation. Écrit d'abord dans un fichier temporaire puis le déplace vers son nom définitif
    /// (même idiome que <see cref="Storage.JsonFileStore.WriteAsync{T}"/>) : une annulation ou un
    /// échec en cours de zippage ne laisse jamais de sauvegarde partielle visible dans la liste, le
    /// fichier temporaire est simplement supprimé. Élague ensuite les sauvegardes au-delà de
    /// <see cref="Instances.InstanceBackupSettings.KeepCount"/> (les plus anciennes d'abord, jamais
    /// celle qu'on vient de créer).
    /// </summary>
    /// <exception cref="Instances.InstanceNotFoundException">Aucune instance pour <paramref name="slug"/>.</exception>
    public async Task<InstanceBackupInfo> CreateAsync(
        string slug,
        IProgress<InstanceBackupProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);

        var record = await _instances.LoadAsync(slug, cancellationToken).ConfigureAwait(false);
        var dataDirectory = _instances.GetDataDirectory(slug);
        var backupsDirectory = GetBackupsDirectory(slug);
        _fileSystem.Directory.CreateDirectory(backupsDirectory);

        var fileName = GenerateUniqueFileName(backupsDirectory);
        var targetPath = _fileSystem.Path.Combine(backupsDirectory, fileName);
        var tempPath = targetPath + JsonFileStore.TempFileSuffix;

        var files = _fileSystem.Directory.Exists(dataDirectory)
            ? _fileSystem.Directory.GetFiles(dataDirectory, "*", SearchOption.AllDirectories)
            : [];

        try
        {
            var stream = _fileSystem.File.Create(tempPath);
            await using (stream.ConfigureAwait(false))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                for (var index = 0; index < files.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await AddEntryAsync(archive, dataDirectory, files[index], cancellationToken).ConfigureAwait(false);
                    progress?.Report(new InstanceBackupProgress(index + 1, files.Length));
                }
            }
        }
        catch
        {
            // Le fichier temporaire ne doit jamais rester après une annulation ou un échec : la
            // liste des sauvegardes (qui ne scanne que backups/*.zip, jamais *.tmp) ne doit jamais
            // montrer une archive à moitié écrite.
            RemoveFileIfExists(tempPath);
            throw;
        }

        _fileSystem.File.Move(tempPath, targetPath);

        var createdInfo = new InstanceBackupInfo(fileName, _fileSystem.FileInfo.New(targetPath).Length, _clock.UtcNow);

        await PruneAsync(slug, fileName, record.Metadata.Backups.KeepCount, cancellationToken).ConfigureAwait(false);

        return createdInfo;
    }

    /// <summary>Liste les sauvegardes existantes (nom, taille, date), les plus récentes d'abord. Un dossier <c>backups/</c> absent (aucune sauvegarde) rend une liste vide.</summary>
    public Task<IReadOnlyList<InstanceBackupInfo>> ListAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = GetBackupsDirectory(slug);
        if (!_fileSystem.Directory.Exists(directory))
        {
            return Task.FromResult<IReadOnlyList<InstanceBackupInfo>>([]);
        }

        var infos = _fileSystem.Directory.GetFiles(directory, "*" + ZipExtension)
            .Select(path => _fileSystem.FileInfo.New(path))
            .Select(info => new InstanceBackupInfo(info.Name, info.Length, ParseCreatedUtc(info.Name, info.LastWriteTimeUtc)))
            .OrderByDescending(info => info.CreatedUtc)
            .ThenBy(info => info.FileName, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult<IReadOnlyList<InstanceBackupInfo>>(infos);
    }

    /// <summary>Supprime une sauvegarde.</summary>
    /// <exception cref="InstanceBackupNotFoundException">Aucun fichier <paramref name="fileName"/> dans les sauvegardes de <paramref name="slug"/>.</exception>
    public Task DeleteAsync(string slug, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        cancellationToken.ThrowIfCancellationRequested();

        var path = _fileSystem.Path.Combine(GetBackupsDirectory(slug), fileName);
        if (!_fileSystem.File.Exists(path))
        {
            throw new InstanceBackupNotFoundException(slug, fileName);
        }

        _fileSystem.File.Delete(path);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Restaure <c>data/</c> depuis une sauvegarde, par un échange sûr. L'ordre des opérations est
    /// l'endroit où réfléchir, documenté ici explicitement plutôt que laissé implicite dans le
    /// code :
    /// <list type="number">
    /// <item>
    /// <b>Sauvegarde de sécurité de l'état courant, EN PREMIER, avant que quoi que ce soit ne
    /// touche <c>data/</c>.</b> Si cette étape échoue (disque plein...), on s'arrête là :
    /// <c>data/</c> n'a pas encore bougé, et surtout on n'a PAS de filet pour la suite. Continuer
    /// sans ce filet serait le contraire de ce que cette méthode promet.
    /// </item>
    /// <item>
    /// <b>Extraction de la sauvegarde ciblée vers un dossier de STAGING, jamais dans <c>data/</c>
    /// directement.</b> Toute l'archive est décompressée à côté, avec progression et annulation.
    /// Un échec ou une annulation ici ne laisse <c>data/</c> pas touché du tout : seul le staging
    /// (nettoyé) en a fait les frais.
    /// </item>
    /// <item>
    /// <b>Échange par deux renommages de dossier</b> (jamais une suppression suivie d'une copie
    /// fichier par fichier, dont la fenêtre de risque serait bien plus large) : <c>data/</c> est
    /// d'abord renommé de côté (il continue d'exister sous un autre nom, rien n'est perdu), PUIS le
    /// staging est renommé <c>data/</c> à sa place. Si le second renommage échoue — le seul instant
    /// où <c>data/</c> n'existe momentanément plus sous son nom — le premier est défait
    /// immédiatement (le dossier mis de côté reprend le nom <c>data/</c>) : c'est la sauvegarde de
    /// sécurité de l'étape 1 qui rend cette récupération possible, l'instance ne se retrouve jamais
    /// à moitié écrasée.
    /// </item>
    /// <item>Nettoyage du dossier mis de côté, une fois l'échange confirmé.</item>
    /// </list>
    /// </summary>
    /// <exception cref="InstanceBackupNotFoundException">Aucun fichier <paramref name="fileName"/> dans les sauvegardes de <paramref name="slug"/>.</exception>
    public async Task RestoreAsync(
        string slug,
        string fileName,
        IProgress<InstanceBackupProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var backupPath = _fileSystem.Path.Combine(GetBackupsDirectory(slug), fileName);
        if (!_fileSystem.File.Exists(backupPath))
        {
            throw new InstanceBackupNotFoundException(slug, fileName);
        }

        // Étape 1 : voir la docstring de cette méthode. Ni progress ni cancellationToken ne sont
        // transmis à cette création : une annulation demandée par le joueur ne doit jamais couper
        // la prise du filet de sécurité en plein milieu (une sauvegarde de sécurité à moitié faite
        // ne protège rien). CancellationToken.None est volontaire, pas un oubli.
        await CreateAsync(slug, progress: null, CancellationToken.None).ConfigureAwait(false);

        var dataDirectory = _instances.GetDataDirectory(slug);
        var stagingDirectory = dataDirectory + StagingDirectorySuffix;
        var asideDirectory = dataDirectory + AsideDirectorySuffix;

        // Résidus d'une tentative précédente interrompue avant d'avoir pu nettoyer : jamais
        // supposés propres.
        RemoveDirectoryIfExists(stagingDirectory);
        RemoveDirectoryIfExists(asideDirectory);

        // Étape 2.
        try
        {
            await ExtractAsync(backupPath, stagingDirectory, progress, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RemoveDirectoryIfExists(stagingDirectory);
            throw;
        }

        // Étape 3.
        if (_fileSystem.Directory.Exists(dataDirectory))
        {
            _fileSystem.Directory.Move(dataDirectory, asideDirectory);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.Directory.Move(stagingDirectory, dataDirectory);
        }
        catch
        {
            // data/ a été retiré (renommé) mais son remplaçant n'a pas pu prendre sa place : on
            // annule le premier renommage plutôt que de laisser l'instance sans dossier data/ du
            // tout — c'est exactement le rôle de l'avoir mis de côté plutôt que supprimé.
            if (!_fileSystem.Directory.Exists(dataDirectory) && _fileSystem.Directory.Exists(asideDirectory))
            {
                _fileSystem.Directory.Move(asideDirectory, dataDirectory);
            }

            RemoveDirectoryIfExists(stagingDirectory);
            throw;
        }

        // Étape 4.
        RemoveDirectoryIfExists(asideDirectory);
    }

    private async Task ExtractAsync(
        string backupPath,
        string destinationDirectory,
        IProgress<InstanceBackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        _fileSystem.Directory.CreateDirectory(destinationDirectory);

        var stream = _fileSystem.File.OpenRead(backupPath);
        await using (stream.ConfigureAwait(false))
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entries = archive.Entries.Where(entry => entry.Name.Length > 0).ToArray();

            for (var index = 0; index < entries.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ExtractEntryAsync(entries[index], destinationDirectory, cancellationToken).ConfigureAwait(false);
                progress?.Report(new InstanceBackupProgress(index + 1, entries.Length));
            }
        }
    }

    private async Task ExtractEntryAsync(ZipArchiveEntry entry, string destinationDirectory, CancellationToken cancellationToken)
    {
        var relative = entry.FullName.Replace('/', _fileSystem.Path.DirectorySeparatorChar);
        var destinationPath = _fileSystem.Path.Combine(destinationDirectory, relative);
        var destinationDir = _fileSystem.Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir))
        {
            _fileSystem.Directory.CreateDirectory(destinationDir);
        }

        var entryStream = entry.Open();
        await using (entryStream.ConfigureAwait(false))
        {
            var destinationStream = _fileSystem.File.Create(destinationPath);
            await using (destinationStream.ConfigureAwait(false))
            {
                await entryStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task AddEntryAsync(ZipArchive archive, string dataDirectory, string filePath, CancellationToken cancellationToken)
    {
        var relative = _fileSystem.Path.GetRelativePath(dataDirectory, filePath).Replace(_fileSystem.Path.DirectorySeparatorChar, '/');
        var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);

        var entryStream = entry.Open();
        await using (entryStream.ConfigureAwait(false))
        {
            var sourceStream = _fileSystem.File.OpenRead(filePath);
            await using (sourceStream.ConfigureAwait(false))
            {
                await sourceStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Élague les plus anciennes sauvegardes au-delà de keepCount, sans jamais retirer celle qu'on
    // vient de créer (exclue des candidates avant même de trier par ancienneté).
    private async Task PruneAsync(string slug, string justCreatedFileName, int keepCount, CancellationToken cancellationToken)
    {
        var backups = await ListAsync(slug, cancellationToken).ConfigureAwait(false);
        var excess = backups.Count - Math.Max(keepCount, InstanceBackupSettings.MinKeepCount);
        if (excess <= 0)
        {
            return;
        }

        var directory = GetBackupsDirectory(slug);
        var deletable = backups
            .Where(backup => !string.Equals(backup.FileName, justCreatedFileName, StringComparison.Ordinal))
            .OrderBy(backup => backup.CreatedUtc)
            .ThenBy(backup => backup.FileName, StringComparer.Ordinal)
            .Take(excess);

        foreach (var backup in deletable)
        {
            RemoveFileIfExists(_fileSystem.Path.Combine(directory, backup.FileName));
        }
    }

    // Même idiome que InstanceSlugGenerator.GenerateUnique : le nom de base d'abord, un suffixe
    // numérique croissant seulement en cas de collision (deux créations dans la même seconde
    // d'horloge — un test avec IClock figé peut tout à fait produire ce cas).
    private string GenerateUniqueFileName(string backupsDirectory)
    {
        var baseName = _clock.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var candidate = baseName + ZipExtension;
        var suffix = 2;
        while (_fileSystem.File.Exists(_fileSystem.Path.Combine(backupsDirectory, candidate)))
        {
            candidate = string.Create(CultureInfo.InvariantCulture, $"{baseName}-{suffix}{ZipExtension}");
            suffix++;
        }

        return candidate;
    }

    // Le nom du fichier porte l'horodatage voulu dès la création (GenerateUniqueFileName) : le
    // relire depuis le nom plutôt que depuis les métadonnées du système de fichiers garde
    // CreateAsync et ListAsync cohérents entre eux, et survit à une copie du fichier qui
    // changerait sa date d'écriture. Repli sur lastWriteTimeUtc si le fichier ne suit pas la
    // convention (déposé à la main) : jamais d'exception pour un fichier surnuméraire, même
    // principe de tolérance que BrokenInstance côté scan d'instances.
    private static DateTimeOffset ParseCreatedUtc(string fileName, DateTimeOffset lastWriteTimeUtc)
    {
        var baseName = fileName.EndsWith(ZipExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^ZipExtension.Length]
            : fileName;

        if (baseName.Length < TimestampFormat.Length)
        {
            return lastWriteTimeUtc;
        }

        var datePart = baseName[..TimestampFormat.Length];

        return DateTimeOffset.TryParseExact(
            datePart,
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : lastWriteTimeUtc;
    }

    private void RemoveFileIfExists(string path)
    {
        if (_fileSystem.File.Exists(path))
        {
            _fileSystem.File.Delete(path);
        }
    }

    private void RemoveDirectoryIfExists(string path)
    {
        if (_fileSystem.Directory.Exists(path))
        {
            _fileSystem.Directory.Delete(path, recursive: true);
        }
    }
}