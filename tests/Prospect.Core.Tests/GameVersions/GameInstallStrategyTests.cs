using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.GameVersions;
using Prospect.Core.Storage;

using Shouldly;

namespace Prospect.Core.Tests.GameVersions;

public sealed class GameInstallStrategyTests
{
    private const UnixFileMode Mode755 =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private const string ArchivePath = "/data/prospect/cache/downloads/vs_client_linux-x64_1.22.6.tar.gz";
    private const string TargetDirectory = "/data/prospect/versions/1.22.6";

    private static MockFileSystem WithArchive(byte[] archive)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(ArchivePath, new MockFileData(archive));

        return fileSystem;
    }

    [Fact]
    public async Task LinuxStrategy_RealTarGz_ExtractsEveryFileWithItsContent()
    {
        var archive = TarGzSamples.Create(
            ("Vintagestory", TarGzSamples.Text("#!/bin/sh")),
            ("assets/game/lang/fr.json", TarGzSamples.Text("{}")));
        var fileSystem = WithArchive(archive);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, CancellationToken.None);

        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "Vintagestory")).ShouldBe("#!/bin/sh");
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "assets", "game", "lang", "fr.json")).ShouldBe("{}");
    }

    [Fact]
    public async Task LinuxStrategy_DirectoryEntries_AreCreatedEvenWhenEmpty()
    {
        var archive = TarGzSamples.Create(("Mods/", null), ("Vintagestory", TarGzSamples.Text("x")));
        var fileSystem = WithArchive(archive);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, CancellationToken.None);

        fileSystem.Directory.Exists(fileSystem.Path.Combine(TargetDirectory, "Mods")).ShouldBeTrue();
    }

    [Fact]
    public async Task LinuxStrategy_AfterExtraction_PutsTheExecutableBitsBackOnEverything()
    {
        var archive = TarGzSamples.Create(
            ("Vintagestory", TarGzSamples.Text("#!/bin/sh")),
            ("assets/game/lang/fr.json", TarGzSamples.Text("{}")));
        var fileSystem = WithArchive(archive);
        var permissions = new RecordingUnixFilePermissions();
        var strategy = new LinuxGameInstallStrategy(fileSystem, permissions);

        await strategy.InstallAsync(ArchivePath, TargetDirectory, CancellationToken.None);

        // Les chemins posés sont ceux de la cible normalisée : sur Windows, « /data/... » devient
        // « C:\data\... », d'où la comparaison à partir de GetFullPath plutôt que du chemin brut.
        var root = fileSystem.Path.GetFullPath(TargetDirectory);
        permissions.Modes.Values.ShouldAllBe(mode => mode == Mode755);
        permissions.Modes.Keys.ShouldContain(root);
        permissions.Modes.Keys.ShouldContain(fileSystem.Path.Combine(root, "Vintagestory"));
        permissions.Modes.Keys.ShouldContain(fileSystem.Path.Combine(root, "assets"));
    }

    [Fact]
    public async Task LinuxStrategy_ArchiveEntryEscapingTheTarget_IsIgnored()
    {
        var archive = TarGzSamples.Create(
            ("../piege.sh", TarGzSamples.Text("rm -rf")),
            ("Vintagestory", TarGzSamples.Text("ok")));
        var fileSystem = WithArchive(archive);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, CancellationToken.None);

        fileSystem.File.Exists("/data/prospect/versions/piege.sh").ShouldBeFalse();
        fileSystem.File.Exists(fileSystem.Path.Combine(TargetDirectory, "Vintagestory")).ShouldBeTrue();
    }

    [Fact]
    public async Task LinuxStrategy_ArchiveThatIsNotGzip_FailsWithTheTypedInstallError()
    {
        var fileSystem = WithArchive([1, 2, 3, 4, 5, 6, 7, 8]);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        var exception = await Should.ThrowAsync<GameInstallFailedException>(
            () => strategy.InstallAsync(ArchivePath, TargetDirectory, CancellationToken.None));

        exception.InnerException.ShouldBeOfType<InvalidDataException>();
    }

    [Fact]
    public void LinuxStrategy_AsksTheCatalogForTheLinuxBuild()
        => new LinuxGameInstallStrategy(new MockFileSystem(), new RecordingUnixFilePermissions())
            .PlatformKeys.ShouldBe([GamePlatforms.Linux]);

    [Fact]
    public void MacStrategy_PrefersAppleSiliconAndFallsBackToIntel()
        => new MacOsGameInstallStrategy(new MockFileSystem(), new RecordingUnixFilePermissions())
            .PlatformKeys.ShouldBe([GamePlatforms.MacArm64, GamePlatforms.MacX64]);

    [Fact]
    public async Task MacStrategy_UsesTheSameExtractionAsLinux()
    {
        var archive = TarGzSamples.Create(("Vintagestory.app/Contents/MacOS/Vintagestory", TarGzSamples.Text("mach-o")));
        var fileSystem = WithArchive(archive);
        var permissions = new RecordingUnixFilePermissions();

        await new MacOsGameInstallStrategy(fileSystem, permissions).InstallAsync(ArchivePath, TargetDirectory, CancellationToken.None);

        fileSystem.File.Exists(fileSystem.Path.Combine(TargetDirectory, "Vintagestory.app", "Contents", "MacOS", "Vintagestory")).ShouldBeTrue();
        permissions.Modes.Values.ShouldAllBe(mode => mode == Mode755);
    }

    [Fact]
    public async Task WindowsStrategy_RunsTheInnoInstallerSilentlyIntoTheVersionFolder()
    {
        var fileSystem = new MockFileSystem();
        var runner = new FakeProcessRunner();
        const string installer = "/data/prospect/cache/downloads/vs_install_win-x64_1.22.6.exe";

        await new WindowsGameInstallStrategy(fileSystem, runner).InstallAsync(installer, TargetDirectory, CancellationToken.None);

        var request = runner.Requests.ShouldHaveSingleItem();
        request.FileName.ShouldBe(installer);
        request.Arguments.ShouldBe(
        [
            "/VERYSILENT",
            "/SUPPRESSMSGBOXES",
            "/NORESTART",
            "/CURRENTUSER",
            "/NOICONS",
            $"/DIR={TargetDirectory}",
        ]);
    }

    [Fact]
    public async Task WindowsStrategy_InstallerFails_SurfacesTheExitCodeAndItsOutput()
    {
        var runner = new FakeProcessRunner { ExitCode = 5, StandardError = "annulé par l'utilisateur" };

        var exception = await Should.ThrowAsync<GameInstallFailedException>(
            () => new WindowsGameInstallStrategy(new MockFileSystem(), runner).InstallAsync("setup.exe", TargetDirectory, CancellationToken.None));

        exception.Message.ShouldContain("5");
        exception.Message.ShouldContain("annulé par l'utilisateur");
    }

    [Fact]
    public void WindowsStrategy_AsksTheCatalogForTheWindowsInstaller()
        => new WindowsGameInstallStrategy(new MockFileSystem(), new FakeProcessRunner())
            .PlatformKeys.ShouldBe([GamePlatforms.Windows]);

    [Fact]
    public void WindowsStrategy_NullArguments_ThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new WindowsGameInstallStrategy(null!, new FakeProcessRunner()));
        Should.Throw<ArgumentNullException>(() => new WindowsGameInstallStrategy(new MockFileSystem(), null!));
    }

    [Fact]
    public void Selector_MapsEachOperatingSystemToItsOwnStrategy()
    {
        var fileSystem = new MockFileSystem();
        var permissions = new RecordingUnixFilePermissions();
        var linux = new LinuxGameInstallStrategy(fileSystem, permissions);
        var windows = new WindowsGameInstallStrategy(fileSystem, new FakeProcessRunner());
        var mac = new MacOsGameInstallStrategy(fileSystem, permissions);
        var selector = new GameInstallStrategySelector(linux, windows, mac);

        selector.Resolve(AppOperatingSystem.Linux).ShouldBeSameAs(linux);
        selector.Resolve(AppOperatingSystem.Windows).ShouldBeSameAs(windows);
        selector.Resolve(AppOperatingSystem.MacOs).ShouldBeSameAs(mac);
    }

    [Fact]
    public void Selector_UnknownOperatingSystem_Throws()
    {
        var fileSystem = new MockFileSystem();
        var permissions = new RecordingUnixFilePermissions();
        var selector = new GameInstallStrategySelector(
            new LinuxGameInstallStrategy(fileSystem, permissions),
            new WindowsGameInstallStrategy(fileSystem, new FakeProcessRunner()),
            new MacOsGameInstallStrategy(fileSystem, permissions));

        Should.Throw<ArgumentOutOfRangeException>(() => selector.Resolve((AppOperatingSystem)99));
    }

    [Fact]
    public void Selector_NullArguments_ThrowArgumentNullException()
    {
        var fileSystem = new MockFileSystem();
        var permissions = new RecordingUnixFilePermissions();
        var linux = new LinuxGameInstallStrategy(fileSystem, permissions);
        var windows = new WindowsGameInstallStrategy(fileSystem, new FakeProcessRunner());
        var mac = new MacOsGameInstallStrategy(fileSystem, permissions);

        Should.Throw<ArgumentNullException>(() => new GameInstallStrategySelector(null!, windows, mac));
        Should.Throw<ArgumentNullException>(() => new GameInstallStrategySelector(linux, null!, mac));
        Should.Throw<ArgumentNullException>(() => new GameInstallStrategySelector(linux, windows, null!));
    }
}