using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;

using Prospect.Core.Auth;
using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Launching;
using Prospect.Core.Runtime;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Auth;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Http;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Launching;

public sealed class GameLauncherTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly GameVersion SampleVersion = GameVersion.Parse("1.21.3");

    private static readonly VsSession Session = new()
    {
        Email = "joueuse@example.invalid",
        PlayerName = "Sylve",
        PlayerUid = "3f2b8e14",
        Entitlements = "singleplayer,multiplayer",
        SessionKey = "cle-de-session",
        SessionSignature = "signature-de-session",
        MpToken = "jeton-multijoueur",
        HostGameServer = "true",
    };

    private sealed record Fixture(
        GameLauncher Launcher,
        InstanceService InstanceService,
        IInstanceRepository InstanceRepository,
        IInstalledGameVersionRepository VersionRepository,
        MockFileSystem FileSystem,
        FakeClock Clock,
        FakeProcessRunner ProcessRunner,
        FakeDotnetLocator DotnetLocator,
        RunningInstanceTracker Tracker,
        VsAccountService Accounts,
        FakeSecretStore SecretStore);

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
        var secretStore = new FakeSecretStore();
        var accounts = new VsAccountService(
            new VsAccountClient(new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Text("{}")), disposeHandler: false)),
            secretStore);
        var clientSettings = new ClientSettingsSessionWriter(fileSystem, new JsonFileStore(fileSystem));

        var launcher = new GameLauncher(
            instanceRepository, versionRepository, dotnetLocator, tracker, strategy, processRunner, fileSystem, Paths, clock, accounts, clientSettings);

        return new Fixture(
            launcher, instanceService, instanceRepository, versionRepository, fileSystem, clock, processRunner, dotnetLocator, tracker, accounts, secretStore);
    }

    // Connecte le service comme le ferait un vrai démarrage d'application : la session vient du
    // stockage, aucun appel réseau n'est simulé ni nécessaire ici.
    private static async Task SignInAsync(Fixture fixture, VsSession? session = null)
    {
        fixture.SecretStore.Stored = session ?? Session;
        await fixture.Accounts.LoadAsync(CancellationToken.None);
    }

    private static string ClientSettingsPath(Fixture fixture, string slug)
        => fixture.FileSystem.Path.Combine(fixture.InstanceRepository.GetDataDirectory(slug), ClientSettingsSessionWriter.FileName);

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
        var strategy = new LinuxGameLaunchStrategy(fixture.FileSystem);
        var clientSettings = new ClientSettingsSessionWriter(fixture.FileSystem, new JsonFileStore(fixture.FileSystem));

        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            null!, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, strategy,
            fixture.ProcessRunner, fixture.FileSystem, Paths, fixture.Clock, fixture.Accounts, clientSettings));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, null!, fixture.DotnetLocator, fixture.Tracker, strategy,
            fixture.ProcessRunner, fixture.FileSystem, Paths, fixture.Clock, fixture.Accounts, clientSettings));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, null!, fixture.Tracker, strategy,
            fixture.ProcessRunner, fixture.FileSystem, Paths, fixture.Clock, fixture.Accounts, clientSettings));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, null!, strategy,
            fixture.ProcessRunner, fixture.FileSystem, Paths, fixture.Clock, fixture.Accounts, clientSettings));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, null!,
            fixture.ProcessRunner, fixture.FileSystem, Paths, fixture.Clock, fixture.Accounts, clientSettings));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, strategy,
            null!, fixture.FileSystem, Paths, fixture.Clock, fixture.Accounts, clientSettings));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, strategy,
            fixture.ProcessRunner, null!, Paths, fixture.Clock, fixture.Accounts, clientSettings));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, strategy,
            fixture.ProcessRunner, fixture.FileSystem, null!, fixture.Clock, fixture.Accounts, clientSettings));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, strategy,
            fixture.ProcessRunner, fixture.FileSystem, Paths, null!, fixture.Accounts, clientSettings));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, strategy,
            fixture.ProcessRunner, fixture.FileSystem, Paths, fixture.Clock, null!, clientSettings));
        Should.Throw<ArgumentNullException>(() => new GameLauncher(
            fixture.InstanceRepository, fixture.VersionRepository, fixture.DotnetLocator, fixture.Tracker, strategy,
            fixture.ProcessRunner, fixture.FileSystem, Paths, fixture.Clock, fixture.Accounts, null!));
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
    public async Task LaunchAsync_NoAccountSignedIn_LeavesTheDataPathCompletelyUntouched()
    {
        // Le comportement d'avant le chantier compte, mot pour mot : sans session, aucun fichier de
        // réglages du jeu n'est créé et le jeu démarre non authentifié.
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        fixture.FileSystem.File.Exists(ClientSettingsPath(fixture, slug)).ShouldBeFalse();
        fixture.ProcessRunner.StartRequests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LaunchAsync_AccountSignedIn_WritesTheSessionIntoTheInstanceClientSettings()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        await SignInAsync(fixture);

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var stringSettings = JsonNode.Parse(fixture.FileSystem.File.ReadAllText(ClientSettingsPath(fixture, slug)))!
            .AsObject()["stringSettings"]!.AsObject();
        stringSettings["sessionkey"]!.GetValue<string>().ShouldBe("cle-de-session");
        stringSettings["playername"]!.GetValue<string>().ShouldBe("Sylve");
        stringSettings["useremail"]!.GetValue<string>().ShouldBe("joueuse@example.invalid");
        stringSettings.Count.ShouldBe(8);
    }

    [Fact]
    public async Task LaunchAsync_AccountSignedIn_InjectsIntoTheSameDataPathThatIsPassedOnTheCommandLine()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        await SignInAsync(fixture);

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var dataPath = fixture.ProcessRunner.StartRequests.ShouldHaveSingleItem().Arguments[0]["--dataPath=".Length..];
        fixture.FileSystem.File.Exists(fixture.FileSystem.Path.Combine(dataPath, ClientSettingsSessionWriter.FileName)).ShouldBeTrue();
    }

    [Fact]
    public async Task LaunchAsync_AccountSignedIn_InjectsBeforeTheProcessStarts()
    {
        // L'ordre est tout l'intérêt : le jeu lit ce fichier à son démarrage, l'écrire après le
        // spawn ne servirait qu'à la session suivante.
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        await SignInAsync(fixture);
        var existedAtSpawn = false;
        fixture.ProcessRunner.NextProcessFactory = _ =>
        {
            existedAtSpawn = fixture.FileSystem.File.Exists(ClientSettingsPath(fixture, slug));

            return new FakeRunningProcess();
        };

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        existedAtSpawn.ShouldBeTrue();
    }

    [Fact]
    public async Task LaunchAsync_AccountSignedIn_PreservesTheGameSettingsAlreadyInTheFile()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        await SignInAsync(fixture);
        fixture.FileSystem.AddFile(ClientSettingsPath(fixture, slug), new MockFileData("""
        { "stringSettings": { "language": "fr" }, "intSettings": { "guiScale": 3 } }
        """));

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var document = JsonNode.Parse(fixture.FileSystem.File.ReadAllText(ClientSettingsPath(fixture, slug)))!.AsObject();
        document["intSettings"]!["guiScale"]!.GetValue<int>().ShouldBe(3);
        document["stringSettings"]!["language"]!.GetValue<string>().ShouldBe("fr");
        document["stringSettings"]!["mptoken"]!.GetValue<string>().ShouldBe("jeton-multijoueur");
    }

    [Fact]
    public async Task LaunchAsync_SignedOutAfterHavingBeenSignedIn_StopsInjectingWithoutTouchingTheExistingFile()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        await SignInAsync(fixture);
        await fixture.Accounts.SignOutAsync(CancellationToken.None);
        var untouched = """{ "stringSettings": { "language": "fr" } }""";
        fixture.FileSystem.AddFile(ClientSettingsPath(fixture, slug), new MockFileData(untouched));

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        fixture.FileSystem.File.ReadAllText(ClientSettingsPath(fixture, slug)).ShouldBe(untouched);
    }

    [Fact]
    public async Task LaunchAsync_UnreadableClientSettings_StartsTheGameAnywayAndSaysSoInTheLog()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        await SignInAsync(fixture);
        fixture.FileSystem.AddFile(ClientSettingsPath(fixture, slug), new MockFileData("{ pas du JSON"));

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        fixture.ProcessRunner.StartRequests.ShouldHaveSingleItem();
        fixture.FileSystem.File.ReadAllText(ClientSettingsPath(fixture, slug)).ShouldBe("{ pas du JSON");
        var log = fixture.FileSystem.File.ReadAllText(fixture.Launcher.GetLogFilePath(slug));
        log.ShouldContain("Session multijoueur non injectée");
    }

    [Fact]
    public async Task LaunchAsync_AccountSignedIn_NeverWritesSessionMaterialIntoTheInstanceLog()
    {
        var fixture = CreateFixture();
        var slug = await CreateInstalledInstanceAsync(fixture);
        await SignInAsync(fixture);

        await fixture.Launcher.LaunchAsync(slug, CancellationToken.None);

        var log = fixture.FileSystem.File.ReadAllText(fixture.Launcher.GetLogFilePath(slug));
        log.ShouldNotContain("cle-de-session");
        log.ShouldNotContain("signature-de-session");
        log.ShouldNotContain("jeton-multijoueur");
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