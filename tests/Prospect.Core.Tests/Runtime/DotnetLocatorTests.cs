using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Runtime;
using Prospect.Core.Tests.Common;

using Shouldly;

namespace Prospect.Core.Tests.Runtime;

public sealed class DotnetLocatorTests
{
    private const string InstallDirectory = "/data/prospect/versions/1.21.3";
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    private static (DotnetLocator Locator, MockFileSystem FileSystem, FakeProcessRunner Runner, FakeClock Clock) CreateLocator()
    {
        var fileSystem = new MockFileSystem();
        var runner = new FakeProcessRunner();
        var clock = new FakeClock(Now);

        return (new DotnetLocator(runner, fileSystem, clock), fileSystem, runner, clock);
    }

    private static void WriteRuntimeConfig(MockFileSystem fileSystem, string frameworkName, string version)
    {
        var json = $$"""
            { "runtimeOptions": { "framework": { "name": "{{frameworkName}}", "version": "{{version}}" } } }
            """;
        fileSystem.AddFile(fileSystem.Path.Combine(InstallDirectory, "Vintagestory.runtimeconfig.json"), new MockFileData(json));
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fileSystem = new MockFileSystem();
        var runner = new FakeProcessRunner();
        var clock = new FakeClock(Now);

        Should.Throw<ArgumentNullException>(() => new DotnetLocator(null!, fileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new DotnetLocator(runner, null!, clock));
        Should.Throw<ArgumentNullException>(() => new DotnetLocator(runner, fileSystem, null!));
    }

    [Fact]
    public async Task ReadRequirementAsync_ConfigFilePresent_ParsesFrameworkAndVersion()
    {
        var (locator, fileSystem, _, _) = CreateLocator();
        WriteRuntimeConfig(fileSystem, "Microsoft.NETCore.App", "8.0.10");

        var requirement = await locator.ReadRequirementAsync(InstallDirectory, CancellationToken.None);

        requirement.IsKnown.ShouldBeTrue();
        requirement.FrameworkName.ShouldBe("Microsoft.NETCore.App");
        requirement.Version.ShouldBe(new Version(8, 0, 10));
    }

    [Fact]
    public async Task ReadRequirementAsync_ConfigFileMissing_ReturnsUnknownWithoutThrowing()
    {
        var (locator, _, _, _) = CreateLocator();

        var requirement = await locator.ReadRequirementAsync(InstallDirectory, CancellationToken.None);

        requirement.ShouldBe(GameRuntimeRequirement.Unknown);
    }

    [Fact]
    public async Task GetInstalledRuntimesAsync_RunsDotnetListRuntimes()
    {
        var (locator, _, runner, _) = CreateLocator();
        runner.StandardOutput = "Microsoft.NETCore.App 8.0.10 [/usr/share/dotnet/shared/Microsoft.NETCore.App]";

        var runtimes = await locator.GetInstalledRuntimesAsync(CancellationToken.None);

        runtimes.ShouldBe([new DotnetRuntimeInfo("Microsoft.NETCore.App", new Version(8, 0, 10))]);
        var request = runner.Requests.ShouldHaveSingleItem();
        request.FileName.ShouldBe("dotnet");
        request.Arguments.ShouldBe(["--list-runtimes"]);
    }

    [Fact]
    public async Task GetInstalledRuntimesAsync_CalledTwiceWithinCacheWindow_OnlyRunsProcessOnce()
    {
        var (locator, _, runner, clock) = CreateLocator();
        runner.StandardOutput = "Microsoft.NETCore.App 8.0.10 [/path]";

        await locator.GetInstalledRuntimesAsync(CancellationToken.None);
        clock.UtcNow = Now.Add(DotnetLocator.CacheDuration / 2);
        await locator.GetInstalledRuntimesAsync(CancellationToken.None);

        runner.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetInstalledRuntimesAsync_CalledAfterCacheExpires_RunsProcessAgain()
    {
        var (locator, _, runner, clock) = CreateLocator();
        runner.StandardOutput = "Microsoft.NETCore.App 8.0.10 [/path]";

        await locator.GetInstalledRuntimesAsync(CancellationToken.None);
        clock.UtcNow = Now.Add(DotnetLocator.CacheDuration).AddSeconds(1);
        await locator.GetInstalledRuntimesAsync(CancellationToken.None);

        runner.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task CheckAsync_RequirementUnknown_ReturnsIndeterminateWithoutQueryingInstalledRuntimes()
    {
        var (locator, _, runner, _) = CreateLocator();

        var result = await locator.CheckAsync(InstallDirectory, CancellationToken.None);

        result.Availability.ShouldBe(RuntimeAvailability.Indeterminate);
        runner.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task CheckAsync_ExactVersionInstalled_ReturnsPresent()
    {
        var (locator, fileSystem, runner, _) = CreateLocator();
        WriteRuntimeConfig(fileSystem, "Microsoft.NETCore.App", "8.0.10");
        runner.StandardOutput = "Microsoft.NETCore.App 8.0.10 [/path]";

        var result = await locator.CheckAsync(InstallDirectory, CancellationToken.None);

        result.Availability.ShouldBe(RuntimeAvailability.Present);
        result.Requirement.Version.ShouldBe(new Version(8, 0, 10));
    }

    [Fact]
    public async Task CheckAsync_NewerPatchInstalledSameMajorMinor_ReturnsPresent()
    {
        var (locator, fileSystem, runner, _) = CreateLocator();
        WriteRuntimeConfig(fileSystem, "Microsoft.NETCore.App", "8.0.0");
        runner.StandardOutput = "Microsoft.NETCore.App 8.0.10 [/path]";

        var result = await locator.CheckAsync(InstallDirectory, CancellationToken.None);

        result.Availability.ShouldBe(RuntimeAvailability.Present);
    }

    [Fact]
    public async Task CheckAsync_OnlyOlderVersionInstalled_ReturnsMissing()
    {
        var (locator, fileSystem, runner, _) = CreateLocator();
        WriteRuntimeConfig(fileSystem, "Microsoft.NETCore.App", "8.0.10");
        runner.StandardOutput = "Microsoft.NETCore.App 8.0.0 [/path]";

        var result = await locator.CheckAsync(InstallDirectory, CancellationToken.None);

        result.Availability.ShouldBe(RuntimeAvailability.Missing);
        result.Requirement.FrameworkName.ShouldBe("Microsoft.NETCore.App");
        result.Requirement.Version.ShouldBe(new Version(8, 0, 10));
    }

    [Fact]
    public async Task CheckAsync_OnlyDifferentMajorInstalled_ReturnsMissing()
    {
        var (locator, fileSystem, runner, _) = CreateLocator();
        WriteRuntimeConfig(fileSystem, "Microsoft.NETCore.App", "10.0.0");
        runner.StandardOutput = "Microsoft.NETCore.App 8.0.10 [/path]";

        var result = await locator.CheckAsync(InstallDirectory, CancellationToken.None);

        result.Availability.ShouldBe(RuntimeAvailability.Missing);
    }

    [Fact]
    public async Task CheckAsync_NoRuntimesInstalledAtAll_ReturnsMissing()
    {
        var (locator, fileSystem, runner, _) = CreateLocator();
        WriteRuntimeConfig(fileSystem, "Microsoft.NETCore.App", "8.0.10");
        runner.StandardOutput = string.Empty;

        var result = await locator.CheckAsync(InstallDirectory, CancellationToken.None);

        result.Availability.ShouldBe(RuntimeAvailability.Missing);
    }

    [Fact]
    public async Task CheckAsync_DifferentFrameworkNameInstalled_ReturnsMissing()
    {
        var (locator, fileSystem, runner, _) = CreateLocator();
        WriteRuntimeConfig(fileSystem, "Microsoft.NETCore.App", "8.0.10");
        runner.StandardOutput = "Microsoft.WindowsDesktop.App 8.0.10 [/path]";

        var result = await locator.CheckAsync(InstallDirectory, CancellationToken.None);

        result.Availability.ShouldBe(RuntimeAvailability.Missing);
    }
}