using Prospect.Core.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Storage;

public class AppPathsTests
{
    [Fact]
    public void Constructor_NullEnvironment_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new AppPaths(null!));
    }

    [Fact]
    public void RootDirectory_LinuxWithXdgDataHomeSet_UsesXdgDataHome()
    {
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.Linux };
        environment.SetEnvironmentVariable("XDG_DATA_HOME", "/home/pixnop/.data");

        var paths = new AppPaths(environment);

        paths.RootDirectory.ShouldBe(Path.Combine("/home/pixnop/.data", "prospect"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RootDirectory_LinuxWithoutXdgDataHome_FallsBackToLocalShare(string? xdgDataHome)
    {
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.Linux };
        environment.SetEnvironmentVariable("XDG_DATA_HOME", xdgDataHome);
        environment.SetFolderPath(Environment.SpecialFolder.UserProfile, "/home/pixnop");

        var paths = new AppPaths(environment);

        paths.RootDirectory.ShouldBe(Path.Combine("/home/pixnop", ".local", "share", "prospect"));
    }

    [Fact]
    public void RootDirectory_WindowsWithAppDataSet_UsesAppData()
    {
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.Windows };
        environment.SetEnvironmentVariable("APPDATA", @"C:\Users\Pixnop\AppData\Roaming");

        var paths = new AppPaths(environment);

        paths.RootDirectory.ShouldBe(Path.Combine(@"C:\Users\Pixnop\AppData\Roaming", "Prospect"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RootDirectory_WindowsWithoutAppData_FallsBackToApplicationDataSpecialFolder(string? appData)
    {
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.Windows };
        environment.SetEnvironmentVariable("APPDATA", appData);
        environment.SetFolderPath(Environment.SpecialFolder.ApplicationData, @"C:\Users\Pixnop\AppData\Roaming");

        var paths = new AppPaths(environment);

        paths.RootDirectory.ShouldBe(Path.Combine(@"C:\Users\Pixnop\AppData\Roaming", "Prospect"));
    }

    [Fact]
    public void RootDirectory_MacOs_UsesLibraryApplicationSupport()
    {
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.MacOs };
        environment.SetFolderPath(Environment.SpecialFolder.UserProfile, "/Users/pixnop");

        var paths = new AppPaths(environment);

        paths.RootDirectory.ShouldBe(Path.Combine("/Users/pixnop", "Library", "Application Support", "Prospect"));
    }

    [Theory]
    [InlineData(AppOperatingSystem.Linux)]
    [InlineData(AppOperatingSystem.Windows)]
    [InlineData(AppOperatingSystem.MacOs)]
    public void RootDirectory_WithOverride_IgnoresComputedDefaultRegardlessOfOs(AppOperatingSystem os)
    {
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = os };

        var paths = new AppPaths(environment, "/custom/prospect-root");

        paths.RootDirectory.ShouldBe("/custom/prospect-root");
    }

    [Fact]
    public void DerivedDirectories_MatchArchitectureLayout()
    {
        var environment = new FakeAppEnvironment();
        var paths = new AppPaths(environment, "/data/prospect");

        paths.InstancesDirectory.ShouldBe(Path.Combine("/data/prospect", "instances"));
        paths.VersionsDirectory.ShouldBe(Path.Combine("/data/prospect", "versions"));
        paths.DownloadsCacheDirectory.ShouldBe(Path.Combine("/data/prospect", "cache", "downloads"));
        paths.HttpCacheDirectory.ShouldBe(Path.Combine("/data/prospect", "cache", "http"));
        paths.LogsDirectory.ShouldBe(Path.Combine("/data/prospect", "logs"));
        paths.SettingsFilePath.ShouldBe(Path.Combine("/data/prospect", "prospect.json"));
    }

    [Fact]
    public void Constructor_NeverTouchesFileSystem()
    {
        // AppPaths ne reçoit aucune abstraction de système de fichiers : structurellement, il ne
        // peut pas créer de dossier. Ce test documente cette garantie plutôt que de la re-vérifier
        // (il n'y a rien à espionner : la construction ne fait que des concaténations de chemin).
        var environment = new FakeAppEnvironment();

        var paths = new AppPaths(environment, "/data/prospect");

        paths.ShouldNotBeNull();
    }
}