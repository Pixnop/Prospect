using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

public sealed class InstalledModRepositoryTests
{
    private const string Slug = "homestead-121";

    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private static (FileSystemInstalledModRepository Repository, MockFileSystem FileSystem) Create()
    {
        var fileSystem = new MockFileSystem();
        var store = new JsonFileStore(fileSystem);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, store, new InstanceMetadataMigrationPipeline([]));
        var repository = new FileSystemInstalledModRepository(
            fileSystem,
            instances,
            new ModArchiveReader(fileSystem),
            new DisabledSuffixModStateConvention(),
            store);

        return (repository, fileSystem);
    }

    private static void AddMod(MockFileSystem fileSystem, string fileName, string? modInfoJson, byte[]? icon = null)
        => fileSystem.AddFile(
            fileSystem.Path.Combine(Paths.InstancesDirectory, Slug, "data", "Mods", fileName),
            new MockFileData(ModInfoSamples.BuildArchive(modInfoJson, icon)));

    [Fact]
    public async Task ScanAsync_NoModsDirectory_IsAnEmptyListRatherThanAnError()
    {
        var (repository, _) = Create();

        (await repository.ScanAsync(Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ScanAsync_EnabledAndDisabledArchives_AreBothListedWithTheirState()
    {
        var (repository, fileSystem) = Create();
        AddMod(fileSystem, "configlib-1.12.0.zip", ModInfoSamples.ConfigLib);
        AddMod(fileSystem, "extrainfo-2.2.1.zip.disabled", ModInfoSamples.ExtraInfo);

        var mods = await repository.ScanAsync(Slug, CancellationToken.None);

        mods.Count.ShouldBe(2);
        mods.Single(mod => mod.Identity == "configlib").IsEnabled.ShouldBeTrue();

        var disabled = mods.Single(mod => mod.Identity == "extrainfo");
        disabled.IsEnabled.ShouldBeFalse();
        // Le nom stable ne porte jamais le suffixe : c'est lui la clé de la provenance.
        disabled.FileName.ShouldBe("extrainfo-2.2.1.zip");
    }

    [Fact]
    public async Task ScanAsync_IgnoresFilesThatAreNotModArchives()
    {
        var (repository, fileSystem) = Create();
        AddMod(fileSystem, "configlib-1.12.0.zip", ModInfoSamples.ConfigLib);
        fileSystem.AddFile(
            fileSystem.Path.Combine(Paths.InstancesDirectory, Slug, "data", "Mods", "notes.txt"),
            new MockFileData("des notes"));

        (await repository.ScanAsync(Slug, CancellationToken.None)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task ScanAsync_UnreadableArchive_IsListedAsUnidentifiedWithItsReason()
    {
        var (repository, fileSystem) = Create();
        AddMod(fileSystem, "configlib-1.12.0.zip", ModInfoSamples.ConfigLib);
        fileSystem.AddFile(
            fileSystem.Path.Combine(Paths.InstancesDirectory, Slug, "data", "Mods", "broken.zip"),
            new MockFileData("pas une archive"));

        var mods = await repository.ScanAsync(Slug, CancellationToken.None);

        mods.Count.ShouldBe(2);
        var broken = mods.Single(mod => mod.FileName == "broken.zip");
        broken.IsIdentified.ShouldBeFalse();
        broken.Problem.ShouldBe(ModInfoProblem.UnreadableArchive);
        broken.DisplayName.ShouldBe("broken.zip");
        broken.Identity.ShouldBe("broken.zip");
    }

    [Fact]
    public async Task ScanAsync_ArchiveWithAnIcon_ExposesIt()
    {
        var (repository, fileSystem) = Create();
        byte[] icon = [0x89, 0x50, 0x4E, 0x47];
        AddMod(fileSystem, "configlib-1.12.0.zip", ModInfoSamples.ConfigLib, icon);

        (await repository.ScanAsync(Slug, CancellationToken.None)).Single().Icon.ShouldBe(icon);
    }

    [Fact]
    public async Task ScanAsync_KnownProvenance_IsAttachedToTheMatchingFile()
    {
        var (repository, fileSystem) = Create();
        AddMod(fileSystem, "configlib-1.12.0.zip", ModInfoSamples.ConfigLib);
        await repository.SaveProvenanceAsync(Slug, Provenance("configlib-1.12.0.zip"), CancellationToken.None);

        var mod = (await repository.ScanAsync(Slug, CancellationToken.None)).Single();

        mod.Provenance.ShouldNotBeNull().ReleaseId.ShouldBe(39980);
    }

    [Fact]
    public async Task ScanAsync_ManuallyDroppedArchive_HasNoProvenance()
    {
        var (repository, fileSystem) = Create();
        AddMod(fileSystem, "mystery-1.0.0.zip", ModInfoSamples.JsonPatchesLib);

        (await repository.ScanAsync(Slug, CancellationToken.None)).Single().Provenance.ShouldBeNull();
    }

    [Fact]
    public async Task SetEnabledAsync_Disabling_RenamesTheArchiveWithTheDisabledSuffix()
    {
        var (repository, fileSystem) = Create();
        AddMod(fileSystem, "configlib-1.12.0.zip", ModInfoSamples.ConfigLib);
        var mod = (await repository.ScanAsync(Slug, CancellationToken.None)).Single();

        var updated = await repository.SetEnabledAsync(Slug, mod, enabled: false, CancellationToken.None);

        updated.IsEnabled.ShouldBeFalse();
        updated.FilePath.ShouldEndWith("configlib-1.12.0.zip.disabled");
        fileSystem.File.Exists(mod.FilePath).ShouldBeFalse();
        fileSystem.File.Exists(updated.FilePath).ShouldBeTrue();
    }

    [Fact]
    public async Task SetEnabledAsync_Reenabling_RestoresThePlainArchiveName()
    {
        var (repository, fileSystem) = Create();
        AddMod(fileSystem, "configlib-1.12.0.zip.disabled", ModInfoSamples.ConfigLib);
        var mod = (await repository.ScanAsync(Slug, CancellationToken.None)).Single();

        var updated = await repository.SetEnabledAsync(Slug, mod, enabled: true, CancellationToken.None);

        updated.IsEnabled.ShouldBeTrue();
        updated.FilePath.ShouldEndWith("configlib-1.12.0.zip");
        updated.Info.ShouldNotBeNull().ModId.ShouldBe("configlib");
    }

    [Fact]
    public async Task SetEnabledAsync_AlreadyInTheRequestedState_ChangesNothing()
    {
        var (repository, fileSystem) = Create();
        AddMod(fileSystem, "configlib-1.12.0.zip", ModInfoSamples.ConfigLib);
        var mod = (await repository.ScanAsync(Slug, CancellationToken.None)).Single();

        var updated = await repository.SetEnabledAsync(Slug, mod, enabled: true, CancellationToken.None);

        updated.FilePath.ShouldBe(mod.FilePath);
        fileSystem.File.Exists(mod.FilePath).ShouldBeTrue();
    }

    [Fact]
    public async Task SetEnabledAsync_FileRemovedBehindOurBack_IsReported()
    {
        var (repository, fileSystem) = Create();
        AddMod(fileSystem, "configlib-1.12.0.zip", ModInfoSamples.ConfigLib);
        var mod = (await repository.ScanAsync(Slug, CancellationToken.None)).Single();
        fileSystem.File.Delete(mod.FilePath);

        await Should.ThrowAsync<ModFileNotFoundException>(
            () => repository.SetEnabledAsync(Slug, mod, enabled: false, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAsync_DeletesTheArchiveAndForgetsItsProvenance()
    {
        var (repository, fileSystem) = Create();
        AddMod(fileSystem, "configlib-1.12.0.zip", ModInfoSamples.ConfigLib);
        await repository.SaveProvenanceAsync(Slug, Provenance("configlib-1.12.0.zip"), CancellationToken.None);
        var mod = (await repository.ScanAsync(Slug, CancellationToken.None)).Single();

        await repository.RemoveAsync(Slug, mod, CancellationToken.None);

        fileSystem.File.Exists(mod.FilePath).ShouldBeFalse();
        (await repository.LoadProvenanceAsync(Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveAsync_FileAlreadyGone_IsNotAnError()
    {
        var (repository, fileSystem) = Create();
        AddMod(fileSystem, "configlib-1.12.0.zip", ModInfoSamples.ConfigLib);
        var mod = (await repository.ScanAsync(Slug, CancellationToken.None)).Single();
        fileSystem.File.Delete(mod.FilePath);

        await Should.NotThrowAsync(() => repository.RemoveAsync(Slug, mod, CancellationToken.None));
    }

    [Fact]
    public async Task SaveProvenanceAsync_SameFileTwice_KeepsOnlyTheLatestEntry()
    {
        var (repository, _) = Create();

        await repository.SaveProvenanceAsync(Slug, Provenance("configlib-1.12.0.zip"), CancellationToken.None);
        await repository.SaveProvenanceAsync(
            Slug,
            Provenance("configlib-1.12.0.zip") with { ReleaseId = 40001, Version = ModVersion.Parse("1.13.0") },
            CancellationToken.None);

        var provenance = await repository.LoadProvenanceAsync(Slug, CancellationToken.None);
        provenance.Count.ShouldBe(1);
        provenance["configlib-1.12.0.zip"].ReleaseId.ShouldBe(40001);
    }

    [Fact]
    public void GetProvenanceFilePath_LivesNextToInstanceJsonRatherThanInsideTheGameDataPath()
    {
        // Le jeu écrit librement dans son dataPath : nos métadonnées se tiennent à côté, jamais
        // dedans (docs/architecture.md, arborescence disque).
        var (repository, fileSystem) = Create();

        var path = repository.GetProvenanceFilePath(Slug);

        path.ShouldBe(fileSystem.Path.Combine(
            Paths.InstancesDirectory,
            Slug,
            FileSystemInstalledModRepository.ProvenanceFileName));
        path.ShouldNotStartWith(repository.GetModsDirectory(Slug));
    }

    [Fact]
    public async Task LoadProvenanceAsync_CorruptedFile_DegradesToNoProvenanceRatherThanFailing()
    {
        var (repository, fileSystem) = Create();
        fileSystem.AddFile(repository.GetProvenanceFilePath(Slug), new MockFileData("{ pas du json"));

        (await repository.LoadProvenanceAsync(Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task LoadProvenanceAsync_UnknownSchemaVersion_IsIgnored()
    {
        var (repository, fileSystem) = Create();
        fileSystem.AddFile(
            repository.GetProvenanceFilePath(Slug),
            new MockFileData("""{ "schemaVersion": 99, "mods": [] }"""));

        (await repository.LoadProvenanceAsync(Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    private static ModProvenance Provenance(string fileName) => new()
    {
        FileName = fileName,
        ModId = 1783,
        ModIdString = "configlib",
        ReleaseId = 39980,
        FileId = 88961,
        Version = ModVersion.Parse("1.12.0"),
        InstalledUtc = Noon,
    };
}