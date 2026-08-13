using System.IO.Abstractions;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Storage;

namespace Prospect.Core.ModDb;

/// <summary>
/// Implémentation d'<see cref="IInstalledModRepository"/> adossée au système de fichiers. Elle ne
/// tient aucun état : chaque appel rescanne, ce qui rend le launcher indifférent à ce que
/// l'utilisateur fait à la main dans <c>data/Mods/</c> entre deux consultations.
/// </summary>
public sealed class FileSystemInstalledModRepository : IInstalledModRepository
{
    /// <summary>Nom du fichier de provenance, à côté d'<c>instance.json</c> et hors de portée du jeu.</summary>
    public const string ProvenanceFileName = "prospect-mods.json";

    /// <summary>Sous-dossier du dataPath où le jeu attend les mods.</summary>
    public const string ModsFolderName = "Mods";

    private readonly IFileSystem _fileSystem;
    private readonly IInstanceRepository _instances;
    private readonly ModArchiveReader _archiveReader;
    private readonly IModStateConvention _convention;
    private readonly JsonFileStore _jsonFileStore;
    private readonly IAppLog _log;

    /// <summary>Construit le repository.</summary>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    /// <param name="instances">Topologie des instances, seule à savoir où vit <c>data/</c>.</param>
    /// <param name="archiveReader">Lecteur de <c>modinfo.json</c>.</param>
    /// <param name="convention">Convention d'activation, voir <see cref="IModStateConvention"/>.</param>
    /// <param name="jsonFileStore">Écritures atomiques du fichier de provenance.</param>
    /// <param name="log">Journal de diagnostic ; optionnel, voir la remarque d'<see cref="IAppLog"/>.</param>
    public FileSystemInstalledModRepository(
        IFileSystem fileSystem,
        IInstanceRepository instances,
        ModArchiveReader archiveReader,
        IModStateConvention convention,
        JsonFileStore jsonFileStore,
        IAppLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(archiveReader);
        ArgumentNullException.ThrowIfNull(convention);
        ArgumentNullException.ThrowIfNull(jsonFileStore);

        _fileSystem = fileSystem;
        _instances = instances;
        _archiveReader = archiveReader;
        _convention = convention;
        _jsonFileStore = jsonFileStore;
        _log = log ?? NullAppLog.Instance;
    }

    /// <inheritdoc />
    public string GetModsDirectory(string slug)
        => _fileSystem.Path.Combine(_instances.GetDataDirectory(slug), ModsFolderName);

    /// <inheritdoc />
    public string GetProvenanceFilePath(string slug)
        => _fileSystem.Path.Combine(_instances.GetInstanceDirectory(slug), ProvenanceFileName);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstalledMod>> ScanAsync(string slug, CancellationToken cancellationToken = default)
    {
        var provenance = await LoadProvenanceAsync(slug, cancellationToken).ConfigureAwait(false);
        var directory = GetModsDirectory(slug);
        var mods = new List<InstalledMod>();

        foreach (var path in _convention.EnumerateModFiles(_fileSystem, directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            mods.Add(Describe(path, provenance));
        }

        return mods
            .OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<InstalledMod> SetEnabledAsync(string slug, InstalledMod installedMod, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installedMod);

        if (!_fileSystem.File.Exists(installedMod.FilePath))
        {
            throw new ModFileNotFoundException(installedMod.FilePath);
        }

        var target = _convention.ResolveTargetPath(_fileSystem, GetModsDirectory(slug), installedMod.FilePath, enabled);
        if (string.Equals(target, installedMod.FilePath, StringComparison.Ordinal))
        {
            return installedMod;
        }

        _fileSystem.File.Move(installedMod.FilePath, target, overwrite: true);
        _log.Write(
            AppLogLevel.Info,
            $"Mod {(enabled ? "activé" : "désactivé")} : « {installedMod.FileName} » dans « {slug} ».");

        var provenance = await LoadProvenanceAsync(slug, cancellationToken).ConfigureAwait(false);

        return Describe(target, provenance);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string slug, InstalledMod installedMod, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installedMod);

        if (_fileSystem.File.Exists(installedMod.FilePath))
        {
            _fileSystem.File.Delete(installedMod.FilePath);
        }

        _log.Write(AppLogLevel.Info, $"Mod désinstallé : « {installedMod.FileName} » de « {slug} ».");

        var document = await ReadDocumentAsync(slug, cancellationToken).ConfigureAwait(false);
        var remaining = document.Mods
            .Where(entry => !string.Equals(entry.FileName, installedMod.FileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (remaining.Length != document.Mods.Count)
        {
            await WriteDocumentAsync(slug, document with { Mods = remaining }, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task SaveProvenanceAsync(string slug, ModProvenance provenance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        var document = await ReadDocumentAsync(slug, cancellationToken).ConfigureAwait(false);
        var mods = document.Mods
            .Where(entry => !string.Equals(entry.FileName, provenance.FileName, StringComparison.OrdinalIgnoreCase))
            .Append(provenance)
            .OrderBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await WriteDocumentAsync(slug, document with { Mods = mods }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ModProvenance>> LoadProvenanceAsync(string slug, CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(slug, cancellationToken).ConfigureAwait(false);

        return document.Mods
            .GroupBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
    }

    private InstalledMod Describe(string path, IReadOnlyDictionary<string, ModProvenance> provenance)
    {
        var content = _archiveReader.Read(path);
        var fileName = _convention.GetStableFileName(_fileSystem, path);

        return new InstalledMod
        {
            FilePath = path,
            FileName = fileName,
            IsEnabled = _convention.IsEnabled(path),
            SizeBytes = GetLength(path),
            Info = content.Result.Info,
            Problem = content.Result.Problem,
            Icon = content.Icon,
            Provenance = provenance.GetValueOrDefault(fileName),
        };
    }

    private long GetLength(string path)
    {
        try
        {
            return _fileSystem.FileInfo.New(path).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0L;
        }
    }

    // Un fichier de provenance illisible ne casse rien : c'est un cache, on repart de zéro plutôt
    // que d'empêcher l'utilisateur de voir ses mods.
    private async Task<ModProvenanceDocument> ReadDocumentAsync(string slug, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _jsonFileStore
                .ReadAsync(GetProvenanceFilePath(slug), ModProvenanceJsonContext.Default.ModProvenanceDocument, cancellationToken)
                .ConfigureAwait(false);

            return document?.SchemaVersion == ModProvenanceDocument.CurrentSchemaVersion ? document : new ModProvenanceDocument();
        }
        catch (CorruptedFileException)
        {
            return new ModProvenanceDocument();
        }
    }

    private async Task WriteDocumentAsync(string slug, ModProvenanceDocument document, CancellationToken cancellationToken)
    {
        try
        {
            await _jsonFileStore
                .WriteAsync(GetProvenanceFilePath(slug), document, ModProvenanceJsonContext.Default.ModProvenanceDocument, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Provenance non écrite : le mod est bel et bien installé, seule la future détection de
            // mise à jour sera moins précise. Faire échouer l'installation pour ça serait absurde.
        }
    }
}