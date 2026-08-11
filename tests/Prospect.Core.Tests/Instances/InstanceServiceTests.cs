using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Instances;

public class InstanceServiceTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly GameVersion SampleVersion = GameVersion.Parse("1.21.3");

    private static (InstanceService Service, IInstanceRepository Repository, MockFileSystem FileSystem, FakeClock Clock) CreateService(DateTimeOffset? now = null)
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var clock = new FakeClock(now ?? Now);
        var service = new InstanceService(repository, fileSystem, clock);
        return (service, repository, fileSystem, clock);
    }

    [Fact]
    public void Constructor_NullRepository_ThrowsArgumentNullException()
    {
        var fileSystem = new MockFileSystem();

        Should.Throw<ArgumentNullException>(() => new InstanceService(null!, fileSystem, new FakeClock(Now)));
    }

    [Fact]
    public void Constructor_NullFileSystem_ThrowsArgumentNullException()
    {
        var (_, repository, _, clock) = CreateService();

        Should.Throw<ArgumentNullException>(() => new InstanceService(repository, null!, clock));
    }

    [Fact]
    public void Constructor_NullClock_ThrowsArgumentNullException()
    {
        var (_, repository, fileSystem, _) = CreateService();

        Should.Throw<ArgumentNullException>(() => new InstanceService(repository, fileSystem, null!));
    }

    [Fact]
    public async Task CreateAsync_ValidNameAndVersion_ReturnsRecordWithSlugAndFreshId()
    {
        var (service, _, _, _) = CreateService();

        var record = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        record.Slug.ShouldBe("homestead");
        record.Metadata.Id.ShouldNotBe(Guid.Empty);
        record.Metadata.Name.ShouldBe("Homestead");
        record.Metadata.GameVersion.ShouldBe(SampleVersion);
        record.Metadata.CreatedUtc.ShouldBe(Now);
        record.Metadata.SchemaVersion.ShouldBe(InstanceMetadata.CurrentSchemaVersion);
    }

    [Fact]
    public async Task CreateAsync_UsesDocumentedDefaultsForOptionalFields()
    {
        var (service, _, _, _) = CreateService();

        var record = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        record.Metadata.Icon.ShouldBe(InstanceMetadata.DefaultIcon);
        record.Metadata.Notes.ShouldBe(string.Empty);
        record.Metadata.LastLaunchedUtc.ShouldBeNull();
        record.Metadata.TotalPlaytimeSeconds.ShouldBe(0L);
        record.Metadata.Launch.ShouldBe(InstanceLaunchSettings.Empty);
    }

    [Fact]
    public async Task CreateAsync_PersistsMetadataLoadableFromRepository()
    {
        var (service, repository, _, _) = CreateService();

        var created = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);
        var loaded = await repository.LoadAsync("homestead", CancellationToken.None);

        loaded.ShouldBe(created);
    }

    [Fact]
    public async Task CreateAsync_CreatesDataDirectory()
    {
        var (service, repository, fileSystem, _) = CreateService();

        await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        fileSystem.Directory.Exists(repository.GetDataDirectory("homestead")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_BlankName_ThrowsInstanceNameInvalidException(string name)
    {
        var (service, _, _, _) = CreateService();

        await Should.ThrowAsync<InstanceNameInvalidException>(() => service.CreateAsync(name, SampleVersion, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_BlankName_CreatesNoDirectory()
    {
        var (service, _, fileSystem, _) = CreateService();

        await Should.ThrowAsync<InstanceNameInvalidException>(() => service.CreateAsync("   ", SampleVersion, CancellationToken.None));

        fileSystem.Directory.Exists(Paths.InstancesDirectory).ShouldBeFalse();
    }

    [Fact]
    public async Task CreateAsync_NameCollision_AppendsNumericSuffixToSlug()
    {
        var (service, _, _, _) = CreateService();

        await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);
        var second = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        second.Slug.ShouldBe("homestead-2");
    }

    [Fact]
    public async Task RenameAsync_ValidNewName_UpdatesNameAndKeepsSlug()
    {
        var (service, _, _, _) = CreateService();
        var created = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        var renamed = await service.RenameAsync(created.Slug, "Nouveau nom", CancellationToken.None);

        renamed.Slug.ShouldBe("homestead");
        renamed.Metadata.Name.ShouldBe("Nouveau nom");
        renamed.Metadata.Id.ShouldBe(created.Metadata.Id);
    }

    [Fact]
    public async Task RenameAsync_PersistsNewName()
    {
        var (service, repository, _, _) = CreateService();
        var created = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        await service.RenameAsync(created.Slug, "Nouveau nom", CancellationToken.None);
        var reloaded = await repository.LoadAsync("homestead", CancellationToken.None);

        reloaded.Metadata.Name.ShouldBe("Nouveau nom");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenameAsync_BlankName_ThrowsInstanceNameInvalidException(string blankName)
    {
        var (service, _, _, _) = CreateService();
        var created = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        await Should.ThrowAsync<InstanceNameInvalidException>(() => service.RenameAsync(created.Slug, blankName, CancellationToken.None));
    }

    [Fact]
    public async Task RenameAsync_UnknownSlug_ThrowsInstanceNotFoundException()
    {
        var (service, _, _, _) = CreateService();

        await Should.ThrowAsync<InstanceNotFoundException>(() => service.RenameAsync("ghost", "Nouveau nom", CancellationToken.None));
    }

    [Fact]
    public async Task DuplicateAsync_Success_CopiesAllFilesPreservingContent()
    {
        var (service, repository, fileSystem, _) = CreateService();
        var source = await service.CreateAsync("Source", SampleVersion, CancellationToken.None);
        var sourceData = repository.GetDataDirectory(source.Slug);
        fileSystem.AddFile(fileSystem.Path.Combine(sourceData, "Mods", "carrycapacity.zip"), new MockFileData("mod-content"));
        fileSystem.AddFile(fileSystem.Path.Combine(sourceData, "clientsettings.json"), new MockFileData("{}"));

        var duplicated = await service.DuplicateAsync(source.Slug, "Copie", progress: null, CancellationToken.None);

        var targetData = repository.GetDataDirectory(duplicated.Slug);
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(targetData, "Mods", "carrycapacity.zip")).ShouldBe("mod-content");
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(targetData, "clientsettings.json")).ShouldBe("{}");
    }

    [Fact]
    public async Task DuplicateAsync_Success_AssignsNewIdNameAndResetsStats()
    {
        var (service, repository, fileSystem, clock) = CreateService();
        var source = await service.CreateAsync("Source", SampleVersion, CancellationToken.None);
        var launched = await repository.LoadAsync(source.Slug, CancellationToken.None);
        await repository.SaveAsync(
            launched with { Metadata = launched.Metadata with { LastLaunchedUtc = Now, TotalPlaytimeSeconds = 3600 } },
            CancellationToken.None);
        clock.UtcNow = Now.AddDays(1);

        var duplicated = await service.DuplicateAsync(source.Slug, "Copie", progress: null, CancellationToken.None);

        duplicated.Metadata.Id.ShouldNotBe(source.Metadata.Id);
        duplicated.Metadata.Name.ShouldBe("Copie");
        duplicated.Metadata.LastLaunchedUtc.ShouldBeNull();
        duplicated.Metadata.TotalPlaytimeSeconds.ShouldBe(0L);
        duplicated.Metadata.CreatedUtc.ShouldBe(Now.AddDays(1));
        _ = fileSystem;
    }

    [Fact]
    public async Task DuplicateAsync_Success_ReportsFilesCopiedAndTotal()
    {
        var (service, repository, fileSystem, _) = CreateService();
        var source = await service.CreateAsync("Source", SampleVersion, CancellationToken.None);
        var sourceData = repository.GetDataDirectory(source.Slug);
        fileSystem.AddFile(fileSystem.Path.Combine(sourceData, "a.txt"), new MockFileData("a"));
        fileSystem.AddFile(fileSystem.Path.Combine(sourceData, "b.txt"), new MockFileData("b"));
        var reports = new List<InstanceCopyProgress>();
        var progress = new SynchronousProgress<InstanceCopyProgress>(reports.Add);

        await service.DuplicateAsync(source.Slug, "Copie", progress, CancellationToken.None);

        reports.Count.ShouldBe(2);
        reports.ShouldAllBe(r => r.TotalFiles == 2);
        reports.Select(r => r.FilesCopied).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task DuplicateAsync_NameCollision_AppendsSuffixToTargetSlug()
    {
        var (service, _, _, _) = CreateService();
        var source = await service.CreateAsync("Source", SampleVersion, CancellationToken.None);
        await service.CreateAsync("Copie", SampleVersion, CancellationToken.None);

        var duplicated = await service.DuplicateAsync(source.Slug, "Copie", progress: null, CancellationToken.None);

        duplicated.Slug.ShouldBe("copie-2");
    }

    [Fact]
    public async Task DuplicateAsync_SourceNotFound_ThrowsInstanceNotFoundException()
    {
        var (service, _, _, _) = CreateService();

        await Should.ThrowAsync<InstanceNotFoundException>(() => service.DuplicateAsync("ghost", "Copie", progress: null, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DuplicateAsync_BlankNewName_ThrowsInstanceNameInvalidException(string blankName)
    {
        var (service, _, _, _) = CreateService();
        var source = await service.CreateAsync("Source", SampleVersion, CancellationToken.None);

        await Should.ThrowAsync<InstanceNameInvalidException>(() => service.DuplicateAsync(source.Slug, blankName, progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task DuplicateAsync_EmptySourceData_StillCreatesTargetDataDirectory()
    {
        var (service, repository, fileSystem, _) = CreateService();
        var source = await service.CreateAsync("Source", SampleVersion, CancellationToken.None);

        var duplicated = await service.DuplicateAsync(source.Slug, "Copie", progress: null, CancellationToken.None);

        fileSystem.Directory.Exists(repository.GetDataDirectory(duplicated.Slug)).ShouldBeTrue();
    }

    [Fact]
    public async Task DuplicateAsync_CancelledBeforeStart_RemovesTargetDirectoryAndThrows()
    {
        var (service, repository, fileSystem, _) = CreateService();
        var source = await service.CreateAsync("Source", SampleVersion, CancellationToken.None);
        fileSystem.AddFile(fileSystem.Path.Combine(repository.GetDataDirectory(source.Slug), "a.txt"), new MockFileData("a"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => service.DuplicateAsync(source.Slug, "Copie", progress: null, cts.Token));

        repository.Exists("copie").ShouldBeFalse();
    }

    [Fact]
    public async Task DuplicateAsync_CancelledDuringCopy_RemovesTargetDirectoryEntirelyAndThrows()
    {
        var (service, repository, fileSystem, _) = CreateService();
        var source = await service.CreateAsync("Source", SampleVersion, CancellationToken.None);
        var sourceData = repository.GetDataDirectory(source.Slug);
        fileSystem.AddFile(fileSystem.Path.Combine(sourceData, "a.txt"), new MockFileData("a"));
        fileSystem.AddFile(fileSystem.Path.Combine(sourceData, "b.txt"), new MockFileData("b"));
        fileSystem.AddFile(fileSystem.Path.Combine(sourceData, "c.txt"), new MockFileData("c"));
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<InstanceCopyProgress>(p =>
        {
            if (p.FilesCopied == 1)
            {
                cts.Cancel();
            }
        });

        await Should.ThrowAsync<OperationCanceledException>(() => service.DuplicateAsync(source.Slug, "Copie", progress, cts.Token));

        repository.Exists("copie").ShouldBeFalse();
        fileSystem.Directory.Exists(repository.GetInstanceDirectory("copie")).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ExistingInstance_RemovesInstanceDirectoryEntirely()
    {
        var (service, repository, fileSystem, _) = CreateService();
        var created = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);
        fileSystem.AddFile(fileSystem.Path.Combine(repository.GetDataDirectory(created.Slug), "Saves", "world.vcdbs"), new MockFileData("save"));

        await service.DeleteAsync(created.Slug, CancellationToken.None);

        fileSystem.Directory.Exists(repository.GetInstanceDirectory(created.Slug)).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_UnknownSlug_ThrowsInstanceNotFoundException()
    {
        var (service, _, _, _) = CreateService();

        await Should.ThrowAsync<InstanceNotFoundException>(() => service.DeleteAsync("ghost", CancellationToken.None));
    }
}