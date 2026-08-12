using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Migration;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Migration;

public class VslDetectorTests
{
    private const string LinuxRoot = "/home/pixnop/.config";

    private static (VslDetector Detector, MockFileSystem FileSystem, FakeAppEnvironment Environment) CreateDetector()
    {
        var fileSystem = new MockFileSystem();
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.Linux };
        environment.SetEnvironmentVariable("XDG_CONFIG_HOME", LinuxRoot);
        var detector = new VslDetector(fileSystem, environment);

        return (detector, fileSystem, environment);
    }

    [Fact]
    public void Constructor_NullFileSystem_ThrowsArgumentNullException()
    {
        var environment = new FakeAppEnvironment();

        Should.Throw<ArgumentNullException>(() => new VslDetector(null!, environment));
    }

    [Fact]
    public void Constructor_NullEnvironment_ThrowsArgumentNullException()
    {
        var fileSystem = new MockFileSystem();

        Should.Throw<ArgumentNullException>(() => new VslDetector(fileSystem, null!));
    }

    [Fact]
    public async Task DetectAsync_NothingOnDisk_ReturnsNotDetected()
    {
        var (detector, _, _) = CreateDetector();

        var result = await detector.DetectAsync();

        result.IsDetected.ShouldBeFalse();
        result.HasConfigFile.ShouldBeFalse();
        result.RootDirectory.ShouldBe(LinuxRoot);
        result.HasAnyContent.ShouldBeFalse();
    }

    [Fact]
    public async Task DetectAsync_ConfigFilePresentWithInstallationsAndGameVersions_ReturnsRichDetectedState()
    {
        var (detector, fileSystem, _) = CreateDetector();
        WriteConfig(fileSystem, """
        {
          "installations": [
            { "id": "a", "name": "Survie", "path": "/data/survie", "version": "1.20.4" },
            { "id": "b", "name": "Créatif", "path": "/data/creatif", "version": "1.21.3" }
          ],
          "gameVersions": [
            { "version": "1.20.4", "path": "/engines/1.20.4" }
          ]
        }
        """);

        var result = await detector.DetectAsync();

        result.IsDetected.ShouldBeTrue();
        result.HasConfigFile.ShouldBeTrue();
        result.ConfigError.ShouldBeNull();
        result.InstallationCount.ShouldBe(2);
        result.GameVersionCount.ShouldBe(1);
        result.HasAnyContent.ShouldBeTrue();
    }

    [Fact]
    public async Task DetectAsync_ConventionFoldersPresentWithoutConfigFile_ReturnsDetectedWithoutConfig()
    {
        var (detector, fileSystem, _) = CreateDetector();
        fileSystem.AddDirectory(Path.Combine(LinuxRoot, "VSLInstallations"));

        var result = await detector.DetectAsync();

        result.IsDetected.ShouldBeTrue();
        result.HasConfigFile.ShouldBeFalse();
        result.InstallationCount.ShouldBe(0);
        result.HasAnyContent.ShouldBeFalse();
    }

    [Fact]
    public async Task DetectAsync_GameVersionsFolderAloneIsEnoughToDetect()
    {
        var (detector, fileSystem, _) = CreateDetector();
        fileSystem.AddDirectory(Path.Combine(LinuxRoot, "VSLGameVersions"));

        var result = await detector.DetectAsync();

        result.IsDetected.ShouldBeTrue();
    }

    [Fact]
    public async Task DetectAsync_ConfigFileIsInvalidJson_ReturnsDetectedWithConfigError()
    {
        var (detector, fileSystem, _) = CreateDetector();
        WriteConfig(fileSystem, "{ not valid json");

        var result = await detector.DetectAsync();

        result.IsDetected.ShouldBeTrue();
        result.HasConfigFile.ShouldBeTrue();
        result.ConfigError.ShouldNotBeNull();
        result.InstallationCount.ShouldBe(0);
        result.GameVersionCount.ShouldBe(0);
    }

    [Fact]
    public async Task DetectAsync_ConfigFileWithOneCorruptedEntry_StillReturnsTheGoodOnesAndReportsTheIssue()
    {
        var (detector, fileSystem, _) = CreateDetector();
        WriteConfig(fileSystem, """
        {
          "installations": [
            { "id": "a", "name": "Bonne", "path": "/data/a", "version": "1.20.4" },
            { "id": "b", "name": "Sans chemin" }
          ],
          "gameVersions": []
        }
        """);

        var result = await detector.DetectAsync();

        result.InstallationCount.ShouldBe(1);
        result.Issues.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DetectAsync_WithRootOverride_UsesTheOverrideInsteadOfTheComputedDefault()
    {
        var (detector, fileSystem, _) = CreateDetector();
        const string customRoot = "/mnt/backup/vsl-portable";
        fileSystem.AddDirectory(Path.Combine(customRoot, "VSLInstallations"));

        var result = await detector.DetectAsync(customRoot);

        result.IsDetected.ShouldBeTrue();
        result.RootDirectory.ShouldBe(customRoot);
    }

    [Fact]
    public async Task DetectAsync_WithRootOverride_DoesNotLookAtTheDefaultLocation()
    {
        var (detector, fileSystem, _) = CreateDetector();
        WriteConfig(fileSystem, """{ "installations": [ { "id": "a", "path": "/data/a", "version": "1.20.4" } ], "gameVersions": [] }""");

        var result = await detector.DetectAsync("/mnt/backup/empty-folder");

        result.IsDetected.ShouldBeFalse();
    }

    [Theory]
    [InlineData(AppOperatingSystem.Windows)]
    [InlineData(AppOperatingSystem.MacOs)]
    [InlineData(AppOperatingSystem.Linux)]
    public async Task DetectAsync_PerOperatingSystem_LooksAtTheRightDefaultRoot(AppOperatingSystem os)
    {
        var fileSystem = new MockFileSystem();
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = os };
        environment.SetEnvironmentVariable("APPDATA", @"C:\Users\Pixnop\AppData\Roaming");
        environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "/home/pixnop/.config");
        environment.SetFolderPath(Environment.SpecialFolder.UserProfile, os == AppOperatingSystem.MacOs ? "/Users/pixnop" : "/home/pixnop");
        var detector = new VslDetector(fileSystem, environment);
        var expectedRoot = new VslPaths(environment).RootDirectory;
        fileSystem.AddDirectory(Path.Combine(expectedRoot, "VSLInstallations"));

        var result = await detector.DetectAsync();

        result.IsDetected.ShouldBeTrue();
        result.RootDirectory.ShouldBe(expectedRoot);
    }

    private static void WriteConfig(MockFileSystem fileSystem, string json)
    {
        var path = Path.Combine(LinuxRoot, "VSLauncher", "config.json");
        fileSystem.AddFile(path, new MockFileData(json));
    }
}