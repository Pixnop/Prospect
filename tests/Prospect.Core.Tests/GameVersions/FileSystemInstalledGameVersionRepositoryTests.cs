using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.GameVersions;

public sealed class FileSystemInstalledGameVersionRepositoryTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");

    private static FileSystemInstalledGameVersionRepository CreateRepository(MockFileSystem fileSystem)
        => new(fileSystem, Paths);

    private static void AddInstalledVersion(MockFileSystem fileSystem, string version, params (string RelativePath, int Size)[] files)
    {
        var directory = fileSystem.Path.Combine(Paths.VersionsDirectory, version);
        foreach (var (relativePath, size) in files)
        {
            fileSystem.AddFile(fileSystem.Path.Combine(directory, relativePath), new MockFileData(new byte[size]));
        }

        fileSystem.AddFile(
            fileSystem.Path.Combine(directory, FileSystemInstalledGameVersionRepository.CompletionMarkerFileName),
            new MockFileData(version));
    }

    [Fact]
    public void GetVersionDirectory_PutsEachVersionSideBySideUnderVersions()
    {
        var fileSystem = new MockFileSystem();

        CreateRepository(fileSystem)
            .GetVersionDirectory(GameVersion.Parse("1.22.6"))
            .ShouldBe(fileSystem.Path.Combine(Paths.VersionsDirectory, "1.22.6"));
    }

    [Fact]
    public void GetVersionDirectory_KeepsTheChannelSuffixInTheFolderName()
    {
        var fileSystem = new MockFileSystem();

        CreateRepository(fileSystem)
            .GetVersionDirectory(GameVersion.Parse("1.23.0-rc.1"))
            .ShouldBe(fileSystem.Path.Combine(Paths.VersionsDirectory, "1.23.0-rc.1"));
    }

    [Fact]
    public async Task ScanAsync_NoVersionsDirectory_ReturnsAnEmptyResult()
    {
        var result = await CreateRepository(new MockFileSystem()).ScanAsync(CancellationToken.None);

        result.Installed.ShouldBeEmpty();
        result.Broken.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScanAsync_CompletedInstall_IsListedWithItsSizeOnDisk()
    {
        var fileSystem = new MockFileSystem();
        AddInstalledVersion(fileSystem, "1.22.6", ("Vintagestory", 500), ("assets/game/lang.json", 250));

        var result = await CreateRepository(fileSystem).ScanAsync(CancellationToken.None);

        var installed = result.Installed.ShouldHaveSingleItem();
        installed.Version.ShouldBe(GameVersion.Parse("1.22.6"));
        installed.SizeBytes.ShouldBeGreaterThanOrEqualTo(750);
        result.Broken.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScanAsync_FolderWithoutTheCompletionMarker_IsReportedAsBrokenRatherThanInstalled()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(fileSystem.Path.Combine(Paths.VersionsDirectory, "1.21.3", "Vintagestory"), new MockFileData("binaire"));

        var result = await CreateRepository(fileSystem).ScanAsync(CancellationToken.None);

        result.Installed.ShouldBeEmpty();
        var broken = result.Broken.ShouldHaveSingleItem();
        broken.FolderName.ShouldBe("1.21.3");
        broken.Reason.ShouldBe(GameInstallBrokenReason.MissingCompletionMarker);
    }

    [Fact]
    public async Task ScanAsync_FolderThatIsNotAVersionName_IsReportedAsBroken()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(fileSystem.Path.Combine(Paths.VersionsDirectory, "brouillon", "readme.txt"), new MockFileData("x"));

        var result = await CreateRepository(fileSystem).ScanAsync(CancellationToken.None);

        result.Broken.ShouldHaveSingleItem().Reason.ShouldBe(GameInstallBrokenReason.UnreadableVersionName);
    }

    [Fact]
    public async Task ScanAsync_SeveralInstalls_AreSortedByDescendingVersion()
    {
        var fileSystem = new MockFileSystem();
        AddInstalledVersion(fileSystem, "1.20.12", ("Vintagestory", 10));
        AddInstalledVersion(fileSystem, "1.22.6", ("Vintagestory", 10));
        AddInstalledVersion(fileSystem, "1.21.3", ("Vintagestory", 10));

        var result = await CreateRepository(fileSystem).ScanAsync(CancellationToken.None);

        result.Installed.Select(entry => entry.Version.ToString()).ShouldBe(["1.22.6", "1.21.3", "1.20.12"]);
    }

    [Fact]
    public async Task ScanAsync_Canceled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => CreateRepository(new MockFileSystem()).ScanAsync(cancellation.Token));
    }

    [Fact]
    public void IsInstalled_ReflectsThePresenceOfTheCompletionMarker()
    {
        var fileSystem = new MockFileSystem();
        AddInstalledVersion(fileSystem, "1.22.6", ("Vintagestory", 10));
        fileSystem.AddFile(fileSystem.Path.Combine(Paths.VersionsDirectory, "1.21.3", "Vintagestory"), new MockFileData("x"));
        var repository = CreateRepository(fileSystem);

        repository.IsInstalled(GameVersion.Parse("1.22.6")).ShouldBeTrue();
        repository.IsInstalled(GameVersion.Parse("1.21.3")).ShouldBeFalse();
        repository.IsInstalled(GameVersion.Parse("1.19.0")).ShouldBeFalse();
    }

    [Fact]
    public void Find_NotInstalled_ReturnsNull()
        => CreateRepository(new MockFileSystem()).Find(GameVersion.Parse("1.22.6")).ShouldBeNull();

    [Fact]
    public void Find_Installed_DescribesIt()
    {
        var fileSystem = new MockFileSystem();
        AddInstalledVersion(fileSystem, "1.22.6", ("Vintagestory", 128));

        var found = CreateRepository(fileSystem).Find(GameVersion.Parse("1.22.6"));

        found.ShouldNotBeNull();
        found.Directory.ShouldBe(fileSystem.Path.Combine(Paths.VersionsDirectory, "1.22.6"));
        found.SizeBytes.ShouldBeGreaterThanOrEqualTo(128);
    }

    [Fact]
    public void PrepareDirectory_LeftoverFromAnInterruptedInstall_IsWipedFirst()
    {
        var fileSystem = new MockFileSystem();
        var version = GameVersion.Parse("1.22.6");
        var directory = fileSystem.Path.Combine(Paths.VersionsDirectory, "1.22.6");
        fileSystem.AddFile(fileSystem.Path.Combine(directory, "moitie-extrait.bin"), new MockFileData("x"));

        CreateRepository(fileSystem).PrepareDirectory(version);

        fileSystem.Directory.Exists(directory).ShouldBeTrue();
        fileSystem.Directory.GetFiles(directory).ShouldBeEmpty();
    }

    [Fact]
    public async Task MarkCompleteAsync_WritesTheSentinelFile()
    {
        var fileSystem = new MockFileSystem();
        var repository = CreateRepository(fileSystem);
        var version = GameVersion.Parse("1.22.6");

        await repository.MarkCompleteAsync(version, CancellationToken.None);

        repository.IsInstalled(version).ShouldBeTrue();
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(
            repository.GetVersionDirectory(version),
            FileSystemInstalledGameVersionRepository.CompletionMarkerFileName)).ShouldBe("1.22.6");
    }

    [Fact]
    public void Remove_DeletesTheWholeFolder()
    {
        var fileSystem = new MockFileSystem();
        AddInstalledVersion(fileSystem, "1.22.6", ("assets/game/lang.json", 10));
        var repository = CreateRepository(fileSystem);

        repository.Remove(GameVersion.Parse("1.22.6"));

        fileSystem.Directory.Exists(repository.GetVersionDirectory(GameVersion.Parse("1.22.6"))).ShouldBeFalse();
    }

    [Fact]
    public void Remove_VersionThatIsNotInstalled_DoesNothing()
        => Should.NotThrow(() => CreateRepository(new MockFileSystem()).Remove(GameVersion.Parse("1.22.6")));

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new FileSystemInstalledGameVersionRepository(null!, Paths));
        Should.Throw<ArgumentNullException>(() => new FileSystemInstalledGameVersionRepository(new MockFileSystem(), null!));
    }
}