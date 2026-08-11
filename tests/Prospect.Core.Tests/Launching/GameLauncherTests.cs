using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Launching;
using Prospect.Core.Runtime;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Launching;

public sealed class GameLauncherTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly GameVersion SampleVersion = GameVersion.Parse("1.21.3");

    private sealed record Fixture(
        GameLauncher Launcher,
        InstanceService InstanceService,
        IInstanceRepository InstanceRepository,
        IInstalledGameVersionRepository VersionRepository,
        MockFileSystem FileSystem,
        FakeClock Clock,
        FakeProcessRunner ProcessRunner,
        FakeDotnetLocator DotnetLocator,
        RunningInstanceTracker Tracker);

    private static Fixture CreateFixture(Func<IFileSystem, IGameLaunchStrategy>? strategyFactory = null)
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var instanceRepository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var instanceService = new InstanceService(instanceRepository, fileSystem, clock);
        var versionRepository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var processRunner = new FakeProcessRunner();
        var dotnetLocator = new FakeDotnetLocator();
        var tracker = new RunningInstanceTracker(instanceService, clock);
        var strategy = (strategyFactory ?? (fs => new LinuxGameLaunchStrategy(fs)))(fileSystem);

        var launcher = new GameLauncher(instanceRepository, versionRepository, dotnetLocator, tracker, strategy, processRunner, fileSystem, Paths, clock);

        return new Fixture(launcher, instanceService, instanceRepository, versionRepository, fileSystem, clock, processRunner, dotnetLocator, tracker);
    }

    private static async Task<string> CreateInstalledInstanceAsync(Fixture fixture, GameVersion? version = null)
    {
        var gameVersion = version ?? SampleVersion;
        var record = await fixture.InstanceService.CreateAsync("Homestead", gameVersion, CancellationToken.None);
        fixture.VersionRepository.PrepareDirectory(gameVersion);
        await fixture.VersionRepository.MarkCompleteAsync(gameVersion, CancellationToken.None);

        return record.Slug;
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fixture = CreateFixture();

        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            null!, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, new LinuxGameLaunchStrategy(fixture.FileSystem),
            fixture.ProcessRunner, fixture.FileSystem, Paths, fixture.Clock));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, null!, fixture.DotnetLocator, fixture.Tracker, new LinuxGameLaunchStrategy(fixture.FileSystem),
            fixture.ProcessRunner, fixture.FileSystem, Paths, fixture.Clock));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, null!, fixture.Tracker, new LinuxGameLaunchStrategy(fixture.FileSystem),
            fixture.ProcessRunner, fixture.FileSystem, Paths, fixture.Clock));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, null!, new LinuxGameLaunchStrategy(fixture.FileSystem),
            fixture.ProcessRunner, fixture.FileSystem, Paths, fixture.Clock));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, null!,
            fixture.ProcessRunner, fixture.FileSystem, Paths, fixture.Clock));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, new LinuxGameLaunchStrategy(fixture.FileSystem),
            null!, fixture.FileSystem, Paths, fixture.Clock));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, new LinuxGameLaunchStrategy(fixture.FileSystem),
            fixture.ProcessRunner, null!, Paths, fixture.Clock));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, new LinuxGameLaunchStrategy(fixture.FileSystem),
            fixture.ProcessRunner, fixture.FileSystem, null!, fixture.Clock));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, new LinuxGameLaunchStrategy(fixture.FileSystem),
            fixture.ProcessRunner, fixture.FileSystem, Paths, null!));
    }

    [Fact]
    public async Task LaunchAsync_LinuxStrategy_ExecutableIsNativeBinaryAtInstallRoot()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var installDirectory = fixture.VersionRepository.GetVersionDirectory(SampleVersion);
        fixture.ProcessRunner.StartRequests.ShouldHaveSingleItem().FileName
            .ShouldBe(fixture.FileSystem.Path.Combine(installDirectory, "Vintagestory"));
    }

    [Fact]
    public async Task LaunchAsync_WindowsStrategy_ExecutableHasExeExtension()
    {
        var fixture = CreateFixture(strategyFactory: fs => new WindowsGameLaunchStrategy(fs));
        var slug = await CreateInstalledInstanceAsync(fixture);

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var installDirectory = fixture.VersionRepository.GetVersionDirectory(SampleVersion);
        fixture.ProcessRunner.StartRequests.ShouldHaveSingleItem().FileName
            .ShouldBe(fixture.FileSystem.Path.Combine(installDirectory, "Vintagestory.exe"));
    }

    [Fact]
    public async Task LaunchAsync_MacStrategy_ThrowsBeforeStartingAnyProcess()
    {
        var fixture = CreateFixture(strategyFactory: _ => new MacGameLaunchStrategy());
        var slug = await CreateInstalledInstanceAsync(fixture);

        await Should.ThrowAsync<MacLaunchNotSupportedException>(() => fixture.Launcher.LaunchAsync(slug, CancellationToken.None));

        fixture.ProcessRunner.StartRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task LaunchAsync_Success_FirstArgumentIsAbsoluteDataPath()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var request = fixture.ProcessRunner.StartRequests.ShouldHaveSingleItem();
        request.Arguments[0].ShouldBe($"--dataPath={fixture.InstanceRepository.GetDataDirectory(slug)}");
    }

    [Fact]
    public async Task LaunchAsync_InstanceHasExtraArgs_AppendedAfterDataPathInOrderAsSeparateItems()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        await fixture.InstanceService.UpdateLaunchSettingsAsync(
            slug,
            new InstanceLaunchSettings { ExtraArgs = ["--logfile", "custom.log", "--dev"] },
            CancellationToken.None);

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var request = fixture.ProcessRunner.StartRequests.ShouldHaveSingleItem();
        request.Arguments.ShouldBe(
        [
            $"--dataPath={fixture.InstanceRepository.GetDataDirectory(slug)}",
            "--logfile",
            "custom.log",
            "--dev",
        ]);
    }

    [Fact]
    public async Task LaunchAsync_InstanceHasEnvVars_PassedThroughToProcessStartRequest()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        await fixture.InstanceService.UpdateLaunchSettingsAsync(
            slug,
            new InstanceLaunchSettings { Env = new Dictionary<string, string> { ["MESA_GLTHREAD"] = "true" } },
            CancellationToken.None);

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var request = fixture.ProcessRunner.StartRequests.ShouldHaveSingleItem();
        request.EnvironmentVariables.ShouldNotBeNull();
        request.EnvironmentVariables!["MESA_GLTHREAD"].ShouldBe("true");
    }

    [Fact]
    public async Task LaunchAsync_NoExtraArgsOrEnv_OnlyDataPathArgumentAndEmptyEnv()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var request = fixture.ProcessRunner.StartRequests.ShouldHaveSingleItem();
        request.Arguments.Count.ShouldBe(1);
        request.EnvironmentVariables.ShouldNotBeNull();
        request.EnvironmentVariables!.ShouldBeEmpty();
    }

    [Fact]
    public async Task LaunchAsync_AlreadyRunning_ThrowsWithoutStartingASecondProcess()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var exception = await Should.ThrowAsync<InstanceAlreadyRunningException>(() => fixture.Launcher.LaunchAsync(slug, CancellationToken.None));

        exception.Slug.ShouldBe(slug);
        fixture.ProcessRunner.StartRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task LaunchAsync_UnknownInstance_ThrowsInstanceNotFoundException()
    {
        var fixture = CreateFixture();

        await Should.ThrowAsync<InstanceNotFoundException>(() => fixture.Launcher.LaunchAsync("ghost", CancellationToken.None));
    }

    [Fact]
    public async Task LaunchAsync_VersionNeverInstalled_ThrowsGameVersionNotInstalledExceptionBeforeStartingProcess()
    {
        var fixture = CreateFixture();
        var record = await fixture.InstanceService.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        var exception = await Should.ThrowAsync<GameVersionNotInstalledException>(() => fixture.Launcher.LaunchAsync(record.Slug, CancellationToken.None));

        exception.Message.ShouldContain("1.21.3");
        fixture.ProcessRunner.StartRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task LaunchAsync_VersionInstallationInterrupted_MissingSentinel_ThrowsGameVersionNotInstalledException()
    {
        var fixture = CreateFixture();
        var record = await fixture.InstanceService.CreateAsync("Homestead", SampleVersion, CancellationToken.None);
        fixture.VersionRepository.PrepareDirectory(SampleVersion);

        await Should.ThrowAsync<GameVersionNotInstalledException>(() => fixture.Launcher.LaunchAsync(record.Slug, CancellationToken.None));
    }

    [Fact]
    public async Task LaunchAsync_RuntimeMissing_ThrowsRuntimeNotAvailableExceptionBeforeStartingProcess()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        fixture.DotnetLocator.Result = RuntimeCheckResult.Missing(GameRuntimeRequirement.Known("Microsoft.NETCore.App", new Version(8, 0, 10)));

        var exception = await Should.ThrowAsync<RuntimeNotAvailableException>(() => fixture.Launcher.LaunchAsync(slug, CancellationToken.None));

        exception.Message.ShouldContain("Microsoft.NETCore.App");
        exception.Message.ShouldContain("8.0.10");
        fixture.ProcessRunner.StartRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task LaunchAsync_RuntimeIndeterminate_DoesNotBlockLaunch()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        fixture.DotnetLocator.Result = RuntimeCheckResult.Indeterminate;

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        fixture.ProcessRunner.StartRequests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LaunchAsync_Success_ChecksRuntimeAgainstTheInstalledVersionDirectory()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        fixture.DotnetLocator.CheckedDirectories.ShouldHaveSingleItem()
            .ShouldBe(fixture.VersionRepository.GetVersionDirectory(SampleVersion));
    }

    [Fact]
    public async Task LaunchAsync_Success_ReturnsStartedStatusAndIsTrackedAsRunning()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);

        var status = await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        status.State.ShouldBe(RunningInstanceState.Started);
        status.Slug.ShouldBe(slug);
        fixture.Tracker.IsRunning(slug).ShouldBeTrue();
    }

    [Fact]
    public async Task LaunchAsync_Success_MarksInstanceLastLaunchedUtc()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var reloaded = await fixture.InstanceRepository.LoadAsync(slug, CancellationToken.None);
        reloaded.Metadata.LastLaunchedUtc.ShouldBe(Now);
    }

    [Fact]
    public async Task LaunchAsync_Success_WritesTimestampedHeaderAndTruncatesPreviousLogContent()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        var logPath = fixture.Launcher.GetLogFilePath(slug);
        fixture.FileSystem.AddFile(logPath, new MockFileData("contenu du lancement précédent"));

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var content = fixture.FileSystem.File.ReadAllText(logPath);
        content.ShouldNotContain("contenu du lancement précédent");
        content.ShouldContain(Now.ToString("O"));
        content.ShouldContain(slug.Length > 0 ? "Homestead" : string.Empty);
    }

    [Fact]
    public async Task LaunchAsync_LogFilePath_MatchesInstanceSlugConvention()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);

        fixture.Launcher.GetLogFilePath(slug).ShouldBe(fixture.FileSystem.Path.Combine(Paths.LogsDirectory, $"instance-{slug}.log"));
    }

    [Fact]
    public async Task LaunchAsync_ProcessOutputAndErrorLines_AreAppendedToTheLogFile()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        var process = new FakeRunningProcess();
        fixture.ProcessRunner.NextProcessFactory = _ => process;

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);
        process.EmitOutput("[Server Notification] Loaded 42 mods");
        process.EmitError("[Server Warning] mod targets an older version");

        var content = fixture.FileSystem.File.ReadAllText(fixture.Launcher.GetLogFilePath(slug));
        content.ShouldContain("Loaded 42 mods");
        content.ShouldContain("mod targets an older version");
    }
}