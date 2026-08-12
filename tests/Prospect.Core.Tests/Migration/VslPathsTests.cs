using Prospect.Core.Migration;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Migration;

public class VslPathsTests
{
    [Fact]
    public void Constructor_NullEnvironment_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new VslPaths(null!));
    }

    [Fact]
    public void RootDirectory_LinuxWithXdgConfigHomeSet_UsesXdgConfigHome()
    {
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.Linux };
        environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "/home/pixnop/.config");

        var paths = new VslPaths(environment);

        paths.RootDirectory.ShouldBe("/home/pixnop/.config");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RootDirectory_LinuxWithoutXdgConfigHome_FallsBackToDotConfig(string? xdgConfigHome)
    {
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.Linux };
        environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgConfigHome);
        environment.SetFolderPath(Environment.SpecialFolder.UserProfile, "/home/pixnop");

        var paths = new VslPaths(environment);

        paths.RootDirectory.ShouldBe(Path.Combine("/home/pixnop", ".config"));
    }

    [Fact]
    public void RootDirectory_Linux_IsNotTheProspectDataRoot()
    {
        // VS Launcher (Electron appData) et Prospect (XDG_DATA_HOME) ne vivent PAS sous la même
        // racine par défaut sur Linux, même si XDG_DATA_HOME et XDG_CONFIG_HOME sont tous deux
        // définis : c'est le piège documenté par la recherche, un test qui le confond ne le
        // détecterait jamais.
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.Linux };
        environment.SetEnvironmentVariable("XDG_DATA_HOME", "/home/pixnop/.local/share");
        environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "/home/pixnop/.config");

        var vslPaths = new VslPaths(environment);
        var prospectPaths = new AppPaths(environment);

        vslPaths.RootDirectory.ShouldBe("/home/pixnop/.config");
        prospectPaths.RootDirectory.ShouldBe(Path.Combine("/home/pixnop/.local/share", "prospect"));
    }

    [Fact]
    public void RootDirectory_WindowsWithAppDataSet_UsesAppData()
    {
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.Windows };
        environment.SetEnvironmentVariable("APPDATA", @"C:\Users\Pixnop\AppData\Roaming");

        var paths = new VslPaths(environment);

        paths.RootDirectory.ShouldBe(@"C:\Users\Pixnop\AppData\Roaming");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RootDirectory_WindowsWithoutAppData_FallsBackToApplicationDataSpecialFolder(string? appData)
    {
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.Windows };
        environment.SetEnvironmentVariable("APPDATA", appData);
        environment.SetFolderPath(Environment.SpecialFolder.ApplicationData, @"C:\Users\Pixnop\AppData\Roaming");

        var paths = new VslPaths(environment);

        paths.RootDirectory.ShouldBe(@"C:\Users\Pixnop\AppData\Roaming");
    }

    [Fact]
    public void RootDirectory_MacOs_UsesLibraryApplicationSupportWithoutAppNameSuffix()
    {
        // À la différence d'AppPaths (qui ajoute "Prospect"), VslPaths ne doit RIEN ajouter ici :
        // c'est app.getPath("appData") au sens Electron (bare), pas "userData".
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.MacOs };
        environment.SetFolderPath(Environment.SpecialFolder.UserProfile, "/Users/pixnop");

        var paths = new VslPaths(environment);

        paths.RootDirectory.ShouldBe(Path.Combine("/Users/pixnop", "Library", "Application Support"));
    }

    [Theory]
    [InlineData(AppOperatingSystem.Linux)]
    [InlineData(AppOperatingSystem.Windows)]
    [InlineData(AppOperatingSystem.MacOs)]
    public void RootDirectory_WithOverride_IgnoresComputedDefaultRegardlessOfOs(AppOperatingSystem os)
    {
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = os };

        var paths = new VslPaths(environment, "/custom/vsl-root");

        paths.RootDirectory.ShouldBe("/custom/vsl-root");
    }

    [Fact]
    public void DerivedDirectories_MatchDocumentedVslTopology()
    {
        var environment = new FakeAppEnvironment();
        var paths = new VslPaths(environment, "/home/pixnop/.config");

        paths.InstallationsDirectory.ShouldBe(Path.Combine("/home/pixnop/.config", "VSLInstallations"));
        paths.GameVersionsDirectory.ShouldBe(Path.Combine("/home/pixnop/.config", "VSLGameVersions"));
        paths.BackupsDirectory.ShouldBe(Path.Combine("/home/pixnop/.config", "VSLBackups"));
        paths.ConfigFilePath.ShouldBe(Path.Combine("/home/pixnop/.config", "VSLauncher", "config.json"));
    }

    [Fact]
    public void Constructor_NeverTouchesFileSystem()
    {
        var environment = new FakeAppEnvironment();

        var paths = new VslPaths(environment, "/home/pixnop/.config");

        paths.ShouldNotBeNull();
    }
}