using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;

using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;

namespace Prospect.Core.Instances;

/// <summary>
/// Implémentation d'<see cref="IInstanceRepository"/> adossée au système de fichiers réel (via
/// <see cref="IFileSystem"/>). Seul composant du Core à savoir que le dossier d'une instance
/// s'appelle <c>instances/&lt;slug&gt;</c>, que ses métadonnées vivent dans <c>instance.json</c> à
/// côté (jamais dans) de <c>data/</c>, et que ses mondes vivent sous <c>data/Saves/</c>.
/// </summary>
public sealed class FileSystemInstanceRepository : IInstanceRepository
{
    private const string MetadataFileName = "instance.json";
    private const string DataDirectoryName = "data";
    private const string SavesDirectoryName = "Saves";

    private readonly IFileSystem _fileSystem;
    private readonly AppPaths _appPaths;
    private readonly JsonFileStore _jsonFileStore;
    private readonly InstanceMetadataMigrationPipeline _migrationPipeline;

    public FileSystemInstanceRepository(
        IFileSystem fileSystem,
        AppPaths appPaths,
        JsonFileStore jsonFileStore,
        InstanceMetadataMigrationPipeline migrationPipeline)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(jsonFileStore);
        ArgumentNullException.ThrowIfNull(migrationPipeline);

        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _jsonFileStore = jsonFileStore;
        _migrationPipeline = migrationPipeline;
    }

    /// <inheritdoc />
    public string GetInstanceDirectory(string slug)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);

        return _fileSystem.Path.Combine(_appPaths.InstancesDirectory, slug);
    }

    /// <inheritdoc />
    public string GetDataDirectory(string slug) => _fileSystem.Path.Combine(GetInstanceDirectory(slug), DataDirectoryName);

    /// <inheritdoc />
    public bool Exists(string slug) => _fileSystem.Directory.Exists(GetInstanceDirectory(slug));

    /// <inheritdoc />
    public async Task<InstanceScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        var instances = new List<InstanceRecord>();
        var broken = new List<BrokenInstance>();

        if (!_fileSystem.Directory.Exists(_appPaths.InstancesDirectory))
        {
            return new InstanceScanResult(instances, broken);
        }

        foreach (var directory in _fileSystem.Directory.GetDirectories(_appPaths.InstancesDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var slug = _fileSystem.Path.GetFileName(directory);
            await ScanOneAsync(slug, directory, instances, broken, cancellationToken).ConfigureAwait(false);
        }

        return new InstanceScanResult(instances, broken);
    }

    private async Task ScanOneAsync(
        string slug,
        string directory,
        List<InstanceRecord> instances,
        List<BrokenInstance> broken,
        CancellationToken cancellationToken)
    {
        try
        {
            instances.Add(await LoadAsync(slug, cancellationToken).ConfigureAwait(false));
        }
        catch (InstanceNotFoundException)
        {
            broken.Add(new BrokenInstance(directory, InstanceBrokenReason.MissingMetadataFile, "instance.json est absent de ce dossier."));
        }
        catch (CorruptedFileException ex)
        {
            broken.Add(new BrokenInstance(directory, InstanceBrokenReason.CorruptedMetadataFile, ex.Message));
        }
        catch (InstanceSchemaVersionUnsupportedException ex)
        {
            broken.Add(new BrokenInstance(directory, InstanceBrokenReason.UnsupportedSchemaVersion, ex.Message));
        }
    }

    /// <inheritdoc />
    public async Task<InstanceRecord> LoadAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);

        var metadataPath = GetMetadataFilePath(slug);
        if (!_fileSystem.File.Exists(metadataPath))
        {
            throw new InstanceNotFoundException(slug);
        }

        var document = await ReadJsonObjectAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        var schemaVersion = ReadSchemaVersion(document, metadataPath);

        if (schemaVersion > InstanceMetadata.CurrentSchemaVersion)
        {
            throw new InstanceSchemaVersionUnsupportedException(metadataPath, schemaVersion, InstanceMetadata.CurrentSchemaVersion);
        }

        var upToDate = schemaVersion == InstanceMetadata.CurrentSchemaVersion
            ? document
            : _migrationPipeline.Apply(document, schemaVersion, InstanceMetadata.CurrentSchemaVersion, metadataPath);

        var metadata = upToDate.Deserialize(InstanceJsonContext.Default.InstanceMetadata)
            ?? throw new CorruptedFileException(metadataPath, new JsonException($"'{metadataPath}' s'est désérialisé en une instance nulle."));

        var record = new InstanceRecord(slug, metadata);

        if (schemaVersion != InstanceMetadata.CurrentSchemaVersion)
        {
            await SaveAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return record;
    }

    /// <inheritdoc />
    public Task SaveAsync(InstanceRecord instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return _jsonFileStore.WriteAsync(GetMetadataFilePath(instance.Slug), instance.Metadata, InstanceJsonContext.Default.InstanceMetadata, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InstanceWorldFile>> ListWorldsAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        cancellationToken.ThrowIfCancellationRequested();

        var savesDirectory = _fileSystem.Path.Combine(GetDataDirectory(slug), SavesDirectoryName);
        if (!_fileSystem.Directory.Exists(savesDirectory))
        {
            return Task.FromResult<IReadOnlyList<InstanceWorldFile>>([]);
        }

        var worlds = _fileSystem.Directory.GetFiles(savesDirectory)
            .Select(path => _fileSystem.FileInfo.New(path))
            .Select(info => new InstanceWorldFile(info.Name, info.Length, info.LastWriteTimeUtc))
            .ToArray();

        return Task.FromResult<IReadOnlyList<InstanceWorldFile>>(worlds);
    }

    private string GetMetadataFilePath(string slug) => _fileSystem.Path.Combine(GetInstanceDirectory(slug), MetadataFileName);

    private async Task<JsonObject> ReadJsonObjectAsync(string path, CancellationToken cancellationToken)
    {
        var stream = _fileSystem.File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            JsonNode? node;
            try
            {
                node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new CorruptedFileException(path, ex);
            }

            return node as JsonObject
                ?? throw new CorruptedFileException(path, new JsonException($"'{path}' n'est pas un document JSON objet."));
        }
    }

    private static int ReadSchemaVersion(JsonObject document, string path)
    {
        if (document.TryGetPropertyValue("schemaVersion", out var node)
            && node is JsonValue value
            && value.TryGetValue(out int schemaVersion))
        {
            return schemaVersion;
        }

        throw new CorruptedFileException(path, new JsonException($"'{path}' ne contient pas de champ schemaVersion entier exploitable."));
    }
}