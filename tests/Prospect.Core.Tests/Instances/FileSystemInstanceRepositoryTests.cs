using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Instances.Migrations;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Instances;

public class FileSystemInstanceRepositoryTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");

    private static FileSystemInstanceRepository CreateRepository(
        MockFileSystem fileSystem,
        IEnumerable<IInstanceMetadataMigration>? migrations = null)
        => new(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline(migrations ?? []));

    private static InstanceMetadata CreateSampleMetadata(string name = "Homestead") => new()
    {
        SchemaVersion = InstanceMetadata.CurrentSchemaVersion,
        Id = Guid.NewGuid(),
        Name = name,
        GameVersion = GameVersion.Parse("1.21.3"),
        CreatedUtc = new DateTimeOffset(2026, 8, 10, 14, 0, 0, TimeSpan.Zero),
    };

    private static string ToJson(InstanceMetadata metadata)
        => JsonSerializer.Serialize(metadata, InstanceJsonContext.Default.InstanceMetadata);

    [Fact]
    public void Constructor_NullFileSystem_ThrowsArgumentNullException()
    {
        var fileSystem = new MockFileSystem();

        Should.Throw<ArgumentNullException>(() =>
            new FileSystemInstanceRepository(null!, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([])));
    }

    [Fact]
    public void Constructor_NullAppPaths_ThrowsArgumentNullException()
    {
        var fileSystem = new MockFileSystem();

        Should.Throw<ArgumentNullException>(() =>
            new FileSystemInstanceRepository(fileSystem, null!, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([])));
    }

    [Fact]
    public void Constructor_NullJsonFileStore_ThrowsArgumentNullException()
    {
        var fileSystem = new MockFileSystem();

        Should.Throw<ArgumentNullException>(() =>
            new FileSystemInstanceRepository(fileSystem, Paths, null!, new InstanceMetadataMigrationPipeline([])));
    }

    [Fact]
    public void Constructor_NullMigrationPipeline_ThrowsArgumentNullException()
    {
        var fileSystem = new MockFileSystem();

        Should.Throw<ArgumentNullException>(() =>
            new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), null!));
    }

    [Fact]
    public void GetInstanceDirectory_ReturnsSlugUnderInstancesRoot()
    {
        var repository = CreateRepository(new MockFileSystem());

        repository.GetInstanceDirectory("homestead").ShouldBe(Path.Combine("/data/prospect", "instances", "homestead"));
    }

    [Fact]
    public void GetDataDirectory_ReturnsDataUnderInstanceDirectory()
    {
        var repository = CreateRepository(new MockFileSystem());

        repository.GetDataDirectory("homestead").ShouldBe(Path.Combine("/data/prospect", "instances", "homestead", "data"));
    }

    [Fact]
    public void Exists_DirectoryPresent_ReturnsTrue()
    {
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        fileSystem.AddDirectory(repository.GetInstanceDirectory("homestead"));

        repository.Exists("homestead").ShouldBeTrue();
    }

    [Fact]
    public void Exists_DirectoryAbsent_ReturnsFalse()
    {
        var repository = CreateRepository(new MockFileSystem());

        repository.Exists("homestead").ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsMetadata()
    {
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var record = new InstanceRecord("homestead", CreateSampleMetadata());

        await repository.SaveAsync(record, CancellationToken.None);
        var loaded = await repository.LoadAsync("homestead", CancellationToken.None);

        loaded.ShouldBe(record);
    }

    [Fact]
    public async Task SaveAsync_WritesMetadataFileAtExpectedPath()
    {
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var record = new InstanceRecord("homestead", CreateSampleMetadata());

        await repository.SaveAsync(record, CancellationToken.None);

        var expectedPath = fileSystem.Path.Combine(repository.GetInstanceDirectory("homestead"), "instance.json");
        fileSystem.File.Exists(expectedPath).ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_DirectoryMissing_ThrowsInstanceNotFoundException()
    {
        var repository = CreateRepository(new MockFileSystem());

        var exception = await Should.ThrowAsync<InstanceNotFoundException>(() => repository.LoadAsync("ghost", CancellationToken.None));

        exception.Slug.ShouldBe("ghost");
    }

    [Fact]
    public async Task LoadAsync_DirectoryExistsButMetadataFileMissing_ThrowsInstanceNotFoundException()
    {
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        fileSystem.AddDirectory(repository.GetInstanceDirectory("empty-shell"));

        await Should.ThrowAsync<InstanceNotFoundException>(() => repository.LoadAsync("empty-shell", CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_CorruptedJson_ThrowsCorruptedFileException()
    {
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var path = fileSystem.Path.Combine(repository.GetInstanceDirectory("broken"), "instance.json");
        fileSystem.AddFile(path, new MockFileData("{ ceci n'est pas du JSON"));

        var exception = await Should.ThrowAsync<CorruptedFileException>(() => repository.LoadAsync("broken", CancellationToken.None));

        exception.Path.ShouldBe(path);
    }

    [Fact]
    public async Task LoadAsync_JsonRootIsNotAnObject_ThrowsCorruptedFileException()
    {
        // Syntaxiquement du JSON valide (un simple tableau), mais pas la forme objet qu'exige
        // un document instance.json : distinct à la fois du JSON illisible et du schemaVersion
        // manquant.
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var path = fileSystem.Path.Combine(repository.GetInstanceDirectory("array-root"), "instance.json");
        fileSystem.AddFile(path, new MockFileData("[1, 2, 3]"));

        var exception = await Should.ThrowAsync<CorruptedFileException>(() => repository.LoadAsync("array-root", CancellationToken.None));

        exception.Path.ShouldBe(path);
    }

    [Fact]
    public async Task LoadAsync_ValidJsonWithoutSchemaVersionField_ThrowsCorruptedFileException()
    {
        // JSON syntaxiquement valide mais sans schemaVersion (fichier tronqué à la main, ou
        // document totalement étranger) : distinct du cas "JSON illisible" ci-dessus, mais tout
        // aussi impossible à faire progresser dans le pipeline de migrations.
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var path = fileSystem.Path.Combine(repository.GetInstanceDirectory("no-version"), "instance.json");
        fileSystem.AddFile(path, new MockFileData("{ \"name\": \"Homestead\" }"));

        var exception = await Should.ThrowAsync<CorruptedFileException>(() => repository.LoadAsync("no-version", CancellationToken.None));

        exception.Path.ShouldBe(path);
    }

    [Fact]
    public async Task LoadAsync_SchemaVersionNewerThanCurrent_ThrowsSchemaVersionUnsupported()
    {
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var path = fileSystem.Path.Combine(repository.GetInstanceDirectory("future"), "instance.json");
        var json = ToJson(CreateSampleMetadata()).Replace(
            $"\"schemaVersion\":{InstanceMetadata.CurrentSchemaVersion}", "\"schemaVersion\":99", StringComparison.Ordinal);
        fileSystem.AddFile(path, new MockFileData(json));

        var exception = await Should.ThrowAsync<InstanceSchemaVersionUnsupportedException>(
            () => repository.LoadAsync("future", CancellationToken.None));

        exception.FoundSchemaVersion.ShouldBe(99);
        exception.CurrentSchemaVersion.ShouldBe(InstanceMetadata.CurrentSchemaVersion);
    }

    // JSON minimal au schéma COURANT (pas v1) mais sans les champs optionnels : représente un
    // instance.json déjà migré (ou hand-édité) au schéma courant, auquel il ne manque que des
    // champs de référence. Distinct des tests de migration (Migrations/InstanceMetadataV1ToV2MigrationTests.cs
    // et LoadAsync_OlderSchemaWith(out)MigrationRegistered_* ci-dessous) : celui-ci isole le filet
    // Normalized()/désérialisation partielle de la mécanique de migration elle-même.
    private static string PartialJsonAtCurrentSchema() => $$"""
        {
          "schemaVersion": {{InstanceMetadata.CurrentSchemaVersion}},
          "id": "0c9c1f57-8b2e-4f2a-9c41-3d8a12f7b6e0",
          "name": "Homestead 1.21",
          "gameVersion": "1.21.3",
          "createdUtc": "2026-08-10T14:00:00+00:00"
        }
        """;

    [Fact]
    public async Task LoadAsync_PartialJsonMissingOptionalFields_RestoresDocumentedDefaults()
    {
        // Reproduit le piège de désérialisation découvert sur ProspectSettings (voir la docstring
        // de ProspectSettings.Normalized() et d'InstanceMetadata.Normalized()) : un instance.json
        // partiel, ici sans icon/launch/backups/notes (fichier édité à la main, ou écrit par une
        // version antérieure de Prospect qui n'avait pas encore ces champs), ne doit pas laisser
        // ces propriétés à null en aval de LoadAsync.
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var path = fileSystem.Path.Combine(repository.GetInstanceDirectory("partial"), "instance.json");
        fileSystem.AddFile(path, new MockFileData(PartialJsonAtCurrentSchema()));

        var loaded = await repository.LoadAsync("partial", CancellationToken.None);

        loaded.Metadata.Icon.ShouldBe(InstanceMetadata.DefaultIcon);
        loaded.Metadata.Launch.ShouldBe(InstanceLaunchSettings.Empty);
        loaded.Metadata.Backups.ShouldBe(InstanceBackupSettings.Default);
        loaded.Metadata.Notes.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task ScanAsync_PartialJsonMissingOptionalFields_RestoresDocumentedDefaults()
    {
        // Même piège, en passant par ScanAsync plutôt que LoadAsync directement : c'est le chemin
        // réellement emprunté au démarrage de l'application (HomeViewModel.RefreshAsync), il ne
        // doit pas non plus faire remonter d'instance cassée par null de référence.
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var path = fileSystem.Path.Combine(repository.GetInstanceDirectory("partial"), "instance.json");
        fileSystem.AddFile(path, new MockFileData(PartialJsonAtCurrentSchema()));

        var result = await repository.ScanAsync(CancellationToken.None);

        result.BrokenInstances.ShouldBeEmpty();
        var instance = result.Instances.ShouldHaveSingleItem();
        instance.Metadata.Icon.ShouldBe(InstanceMetadata.DefaultIcon);
        instance.Metadata.Launch.ShouldBe(InstanceLaunchSettings.Empty);
        instance.Metadata.Backups.ShouldBe(InstanceBackupSettings.Default);
        instance.Metadata.Notes.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task LoadAsync_OlderSchemaWithMigrationRegistered_MigratesAndPersistsUpgradedFile()
    {
        // Chaîne de deux migrations factices (0 -> 1 -> 2) : la mécanique générique du pipeline
        // s'exerce isolément d'InstanceMetadataV1ToV2Migration elle-même (voir son test dédié,
        // Migrations/InstanceMetadataV1ToV2MigrationTests.cs), ce test-ci ne prouve que
        // « FileSystemInstanceRepository migre puis persiste », pas le contenu d'une migration
        // précise.
        var fileSystem = new MockFileSystem();
        var v0ToV1 = new FakeInstanceMetadataMigration(fromSchemaVersion: 0, markerPropertyName: "migratedFromV0");
        var v1ToV2 = new FakeInstanceMetadataMigration(fromSchemaVersion: 1, markerPropertyName: "migratedFromV1");
        var repository = CreateRepository(fileSystem, [v0ToV1, v1ToV2]);
        var path = fileSystem.Path.Combine(repository.GetInstanceDirectory("legacy"), "instance.json");
        var json = ToJson(CreateSampleMetadata()).Replace(
            $"\"schemaVersion\":{InstanceMetadata.CurrentSchemaVersion}", "\"schemaVersion\":0", StringComparison.Ordinal);
        fileSystem.AddFile(path, new MockFileData(json));

        var loaded = await repository.LoadAsync("legacy", CancellationToken.None);

        loaded.Metadata.SchemaVersion.ShouldBe(InstanceMetadata.CurrentSchemaVersion);
        var persisted = JsonSerializer.Deserialize(fileSystem.File.ReadAllText(path), InstanceJsonContext.Default.InstanceMetadata);
        persisted.ShouldNotBeNull();
        persisted!.SchemaVersion.ShouldBe(InstanceMetadata.CurrentSchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_OlderSchemaWithoutMigrationRegistered_ThrowsSchemaVersionUnsupported()
    {
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var path = fileSystem.Path.Combine(repository.GetInstanceDirectory("legacy"), "instance.json");
        var json = ToJson(CreateSampleMetadata()).Replace(
            $"\"schemaVersion\":{InstanceMetadata.CurrentSchemaVersion}", "\"schemaVersion\":0", StringComparison.Ordinal);
        fileSystem.AddFile(path, new MockFileData(json));

        var exception = await Should.ThrowAsync<InstanceSchemaVersionUnsupportedException>(
            () => repository.LoadAsync("legacy", CancellationToken.None));

        exception.FoundSchemaVersion.ShouldBe(0);
    }

    [Fact]
    public async Task LoadAsync_RealV1ToV2Migration_MigratesWithSaneBackupDefaultsAndPersistsStably()
    {
        // La première vraie migration du projet (voir sa docstring), testée dans les deux sens :
        // v1 chargé → défauts de backups posés → sauvé en v2 ; v2 rechargé ensuite identique.
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem, [new InstanceMetadataV1ToV2Migration()]);
        var path = fileSystem.Path.Combine(repository.GetInstanceDirectory("legacy"), "instance.json");
        fileSystem.AddFile(path, new MockFileData("""
        {
          "schemaVersion": 1,
          "id": "0c9c1f57-8b2e-4f2a-9c41-3d8a12f7b6e0",
          "name": "Homestead 1.21",
          "gameVersion": "1.21.3",
          "createdUtc": "2026-08-10T14:00:00+00:00"
        }
        """));

        var loaded = await repository.LoadAsync("legacy", CancellationToken.None);

        loaded.Metadata.SchemaVersion.ShouldBe(2);
        loaded.Metadata.Backups.AutoBeforeLaunch.ShouldBeFalse();
        loaded.Metadata.Backups.KeepCount.ShouldBe(5);

        // Sauvé en v2 : le fichier sur disque porte désormais le schéma courant et un bloc backups.
        var persisted = System.Text.Json.Nodes.JsonNode.Parse(fileSystem.File.ReadAllText(path))!.AsObject();
        persisted["schemaVersion"]!.GetValue<int>().ShouldBe(2);
        persisted["backups"].ShouldNotBeNull();

        // Rechargé, le document v2 passe directement (sans migration) et reste identique.
        var reloaded = await repository.LoadAsync("legacy", CancellationToken.None);
        reloaded.ShouldBe(loaded);
    }

    [Fact]
    public async Task ScanAsync_InstancesDirectoryMissing_ReturnsEmptyResult()
    {
        var repository = CreateRepository(new MockFileSystem());

        var result = await repository.ScanAsync(CancellationToken.None);

        result.Instances.ShouldBeEmpty();
        result.BrokenInstances.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScanAsync_SingleValidInstance_ReturnsIt()
    {
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var path = fileSystem.Path.Combine(repository.GetInstanceDirectory("homestead"), "instance.json");
        fileSystem.AddFile(path, new MockFileData(ToJson(CreateSampleMetadata())));

        var result = await repository.ScanAsync(CancellationToken.None);

        result.Instances.Count.ShouldBe(1);
        result.Instances[0].Slug.ShouldBe("homestead");
        result.BrokenInstances.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScanAsync_MixOfValidAndBrokenDirectories_PartitionsCorrectly()
    {
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);

        fileSystem.AddFile(
            fileSystem.Path.Combine(repository.GetInstanceDirectory("valid-one"), "instance.json"),
            new MockFileData(ToJson(CreateSampleMetadata("Valid One"))));
        fileSystem.AddFile(
            fileSystem.Path.Combine(repository.GetInstanceDirectory("valid-two"), "instance.json"),
            new MockFileData(ToJson(CreateSampleMetadata("Valid Two"))));
        fileSystem.AddDirectory(repository.GetInstanceDirectory("missing-metadata"));
        fileSystem.AddFile(
            fileSystem.Path.Combine(repository.GetInstanceDirectory("corrupted"), "instance.json"),
            new MockFileData("{ pas du JSON"));
        fileSystem.AddFile(
            fileSystem.Path.Combine(repository.GetInstanceDirectory("too-new"), "instance.json"),
            new MockFileData(ToJson(CreateSampleMetadata()).Replace(
                $"\"schemaVersion\":{InstanceMetadata.CurrentSchemaVersion}", "\"schemaVersion\":99", StringComparison.Ordinal)));

        var result = await repository.ScanAsync(CancellationToken.None);

        result.Instances.Select(i => i.Slug).ShouldBe(["valid-one", "valid-two"], ignoreOrder: true);
        result.BrokenInstances.Count.ShouldBe(3);
        result.BrokenInstances.ShouldContain(b => b.DirectoryPath == repository.GetInstanceDirectory("missing-metadata") && b.Reason == InstanceBrokenReason.MissingMetadataFile);
        result.BrokenInstances.ShouldContain(b => b.DirectoryPath == repository.GetInstanceDirectory("corrupted") && b.Reason == InstanceBrokenReason.CorruptedMetadataFile);
        result.BrokenInstances.ShouldContain(b => b.DirectoryPath == repository.GetInstanceDirectory("too-new") && b.Reason == InstanceBrokenReason.UnsupportedSchemaVersion);
    }

    [Fact]
    public async Task ListWorldsAsync_SavesDirectoryMissing_ReturnsEmptyList()
    {
        var repository = CreateRepository(new MockFileSystem());

        var worlds = await repository.ListWorldsAsync("homestead", CancellationToken.None);

        worlds.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListWorldsAsync_FilesPresent_ReturnsNameSizeAndLastModified()
    {
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var savesDirectory = fileSystem.Path.Combine(repository.GetDataDirectory("homestead"), "Saves");
        var lastModified = new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc);
        fileSystem.AddFile(
            fileSystem.Path.Combine(savesDirectory, "world1.vcdbs"),
            new MockFileData("0123456789") { LastWriteTime = lastModified });

        var worlds = await repository.ListWorldsAsync("homestead", CancellationToken.None);

        worlds.Count.ShouldBe(1);
        worlds[0].FileName.ShouldBe("world1.vcdbs");
        worlds[0].SizeInBytes.ShouldBe(10);
        worlds[0].LastModifiedUtc.UtcDateTime.ShouldBe(lastModified);
    }

    [Fact]
    public async Task ListWorldsAsync_NonRecursive_IgnoresSubdirectoryFiles()
    {
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var savesDirectory = fileSystem.Path.Combine(repository.GetDataDirectory("homestead"), "Saves");
        fileSystem.AddFile(fileSystem.Path.Combine(savesDirectory, "world1.vcdbs"), new MockFileData("abc"));
        fileSystem.AddFile(fileSystem.Path.Combine(savesDirectory, "backups", "old.vcdbs"), new MockFileData("xyz"));

        var worlds = await repository.ListWorldsAsync("homestead", CancellationToken.None);

        worlds.Count.ShouldBe(1);
        worlds[0].FileName.ShouldBe("world1.vcdbs");
    }
}