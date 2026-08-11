using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Launching;
using Prospect.Core.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Launching;

public sealed class GameLaunchStrategyTests
{
    private const string InstallDirectory = "/data/prospect/versions/1.21.3";

    [Fact]
    public void LinuxStrategy_ResolvesNativeBinaryWithoutExtension()
    {
        var fileSystem = new MockFileSystem();

        new LinuxGameLaunchStrategy(fileSystem).ResolveExecutablePath(InstallDirectory)
            .ShouldBe(fileSystem.Path.Combine(InstallDirectory, "Vintagestory"));
    }

    [Fact]
    public void LinuxStrategy_NullFileSystem_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => new LinuxGameLaunchStrategy(null!));

    [Fact]
    public void WindowsStrategy_ResolvesExeExtension()
    {
        var fileSystem = new MockFileSystem();

        new WindowsGameLaunchStrategy(fileSystem).ResolveExecutablePath(InstallDirectory)
            .ShouldBe(fileSystem.Path.Combine(InstallDirectory, "Vintagestory.exe"));
    }

    [Fact]
    public void WindowsStrategy_NullFileSystem_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => new WindowsGameLaunchStrategy(null!));

    [Fact]
    public void MacStrategy_AlwaysThrowsMacLaunchNotSupported()
    {
        var exception = Should.Throw<MacLaunchNotSupportedException>(
            () => new MacGameLaunchStrategy().ResolveExecutablePath(InstallDirectory));

        exception.Message.ShouldContain("macOS");
    }

    [Fact]
    public void Selector_MapsEachOperatingSystemToItsOwnStrategy()
    {
        var fileSystem = new MockFileSystem();
        var linux = new LinuxGameLaunchStrategy(fileSystem);
        var windows = new WindowsGameLaunchStrategy(fileSystem);
        var mac = new MacGameLaunchStrategy();
        var selector = new GameLaunchStrategySelector(linux, windows, mac);

        selector.Resolve(AppOperatingSystem.Linux).ShouldBeSameAs(linux);
        selector.Resolve(AppOperatingSystem.Windows).ShouldBeSameAs(windows);
        selector.Resolve(AppOperatingSystem.MacOs).ShouldBeSameAs(mac);
    }

    [Fact]
    public void Selector_UnknownOperatingSystem_Throws()
    {
        var fileSystem = new MockFileSystem();
        var selector = new GameLaunchStrategySelector(
            new LinuxGameLaunchStrategy(fileSystem),
            new WindowsGameLaunchStrategy(fileSystem),
            new MacGameLaunchStrategy());

        Should.Throw<ArgumentOutOfRangeException>(() => selector.Resolve((AppOperatingSystem)99));
    }

    [Fact]
    public void Selector_NullArguments_ThrowArgumentNullException()
    {
        var fileSystem = new MockFileSystem();
        var linux = new LinuxGameLaunchStrategy(fileSystem);
        var windows = new WindowsGameLaunchStrategy(fileSystem);
        var mac = new MacGameLaunchStrategy();

        Should.Throw<ArgumentNullException>(() => new GameLaunchStrategySelector(null!, windows, mac));
        Should.Throw<ArgumentNullException>(() => new GameLaunchStrategySelector(linux, null!, mac));
        Should.Throw<ArgumentNullException>(() => new GameLaunchStrategySelector(linux, windows, null!));
    }
}