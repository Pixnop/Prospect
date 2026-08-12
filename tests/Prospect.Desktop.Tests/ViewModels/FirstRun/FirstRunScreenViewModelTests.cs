using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Auth;
using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Migration;
using Prospect.Core.Settings;
using Prospect.Core.Settings.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.FirstRun;
using Prospect.Desktop.ViewModels.Migration;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.FirstRun;

/// <summary>
/// <see cref="FirstRunScreenViewModel"/> : la checklist observe les vrais services (aucune
/// version → étape proposée, version installée → cochée, VSL détecté → entrée visible), le champ
/// de réglage (jamais vu par défaut, marqué vu par toute sortie du flux), et l'ouverture du
/// dialogue d'adoption partagé avec Réglages/Accueil.
/// </summary>
public sealed class FirstRunScreenViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        MockFileSystem FileSystem,
        FakeAppEnvironment Environment,
        RecordingOverlayService Overlay,
        SettingsService Settings,
        IInstalledGameVersionRepository GameVersions,
        VslAdoptionService AdoptionService,
        VsAccountService Accounts,
        MemorySecretStore SecretStore);

    private static readonly VsSession Session = new()
    {
        Email = "joueuse@example.invalid",
        PlayerName = "Sylve",
        PlayerUid = "3f2b8e14",
        Entitlements = "singleplayer",
        SessionKey = "cle",
        SessionSignature = "signature",
        MpToken = "jeton",
        HostGameServer = "false",
    };

    // FakeAppEnvironment (Desktop) ne permet pas de poser de variable d'environnement : voir la
    // remarque équivalente de FirstRunViewModelTests, VslPaths retombe donc toujours sur son repli
    // Linux, Path.Combine(GetFolderPath(UserProfile), ".config").
    private static readonly string VslRoot = Path.Combine("/home/test", ".config");

    private static Fixture CreateServices()
    {
        var fileSystem = new MockFileSystem();
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.Linux };
        var clock = new FakeClock(Now);
        var store = new JsonFileStore(fileSystem);
        var instanceRepository = new FileSystemInstanceRepository(fileSystem, Paths, store, new InstanceMetadataMigrationPipeline([]));
        var instanceService = new InstanceService(instanceRepository, fileSystem, clock);
        var gameVersions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var adoptionService = new VslAdoptionService(instanceService, instanceRepository, gameVersions, store, fileSystem, clock);
        var settings = new SettingsService(fileSystem, Paths, store, new SettingsMigrationPipeline([]));
        var secretStore = new MemorySecretStore();
        var accounts = new VsAccountService(new VsAccountClient(new HttpClient()), secretStore);

        return new Fixture(fileSystem, environment, new RecordingOverlayService(), settings, gameVersions, adoptionService, accounts, secretStore);
    }

    // Connecte le service comme le ferait un vrai démarrage : la session vient du stockage, aucun
    // appel réseau n'est nécessaire ni simulé pour ces tests-ci.
    private static async Task SignInAsync(Fixture fixture)
    {
        fixture.SecretStore.Stored = Session;
        await fixture.Accounts.LoadAsync();
    }

    private static FirstRunScreenViewModel CreateViewModel(Fixture fixture)
    {
        var detector = new VslDetector(fixture.FileSystem, fixture.Environment);
        Func<VslDetectionResult, AdoptVslViewModel> adoptFactory = detection => new AdoptVslViewModel(
            detection,
            fixture.AdoptionService,
            fixture.GameVersions,
            fixture.FileSystem,
            fixture.Overlay,
            new RecordingToastService(),
            new ImmediateUiDispatcher());

        return new FirstRunScreenViewModel(fixture.Settings, fixture.GameVersions, Paths, detector, adoptFactory, fixture.Overlay, fixture.Accounts);
    }

    private static void SeedInstalledVersion(MockFileSystem fileSystem, string version)
    {
        var directory = fileSystem.Path.Combine(Paths.VersionsDirectory, version);
        fileSystem.AddFile(fileSystem.Path.Combine(directory, "Vintagestory"), new MockFileData("binaire"));
        fileSystem.AddFile(
            fileSystem.Path.Combine(directory, FileSystemInstalledGameVersionRepository.CompletionMarkerFileName),
            new MockFileData(version));
    }

    private static void WriteVslConfigWithOneInstallation(MockFileSystem fileSystem)
    {
        var path = fileSystem.Path.Combine(VslRoot, "VSLauncher", "config.json");
        fileSystem.AddFile(path, new MockFileData("""
        {
          "installations": [ { "id": "a", "name": "Survie", "path": "/vsl/installations/a", "version": "1.20.4" } ],
          "gameVersions": []
        }
        """));
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fixture = CreateServices();
        var detector = new VslDetector(fixture.FileSystem, fixture.Environment);
        Func<VslDetectionResult, AdoptVslViewModel> adoptFactory = _ => null!;

        Should.Throw<ArgumentNullException>(() => new FirstRunScreenViewModel(null!, fixture.GameVersions, Paths, detector, adoptFactory, fixture.Overlay, fixture.Accounts));
        Should.Throw<ArgumentNullException>(() => new FirstRunScreenViewModel(fixture.Settings, null!, Paths, detector, adoptFactory, fixture.Overlay, fixture.Accounts));
        Should.Throw<ArgumentNullException>(() => new FirstRunScreenViewModel(fixture.Settings, fixture.GameVersions, null!, detector, adoptFactory, fixture.Overlay, fixture.Accounts));
        Should.Throw<ArgumentNullException>(() => new FirstRunScreenViewModel(fixture.Settings, fixture.GameVersions, Paths, null!, adoptFactory, fixture.Overlay, fixture.Accounts));
        Should.Throw<ArgumentNullException>(() => new FirstRunScreenViewModel(fixture.Settings, fixture.GameVersions, Paths, detector, null!, fixture.Overlay, fixture.Accounts));
        Should.Throw<ArgumentNullException>(() => new FirstRunScreenViewModel(fixture.Settings, fixture.GameVersions, Paths, detector, adoptFactory, null!, fixture.Accounts));
        Should.Throw<ArgumentNullException>(() => new FirstRunScreenViewModel(fixture.Settings, fixture.GameVersions, Paths, detector, adoptFactory, fixture.Overlay, null!));
    }

    [Fact]
    public void HasBeenSeen_DefaultSettings_IsFalse()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);

        viewModel.HasBeenSeen.ShouldBeFalse();
    }

    [Fact]
    public async Task InitializeAsync_AlwaysIncludesTheDataFolderStepAlreadyDone()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        var folderStep = viewModel.Steps[0];
        folderStep.IsDone.ShouldBeTrue();
        folderStep.HasAction.ShouldBeFalse();
        folderStep.Subtitle.ShouldBe(viewModel.DataFolderPath);
    }

    [Fact]
    public async Task InitializeAsync_NoVersionInstalled_GameVersionStepIsProposedWithInstallAction()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        var versionStep = viewModel.Steps[1];
        versionStep.IsDone.ShouldBeFalse();
        versionStep.HasAction.ShouldBeTrue();
        versionStep.ActionCommand.ShouldBeSameAs(viewModel.GoToVersionsCommand);
        versionStep.Subtitle.ShouldBe("aucune installée");
    }

    [Fact]
    public async Task InitializeAsync_VersionInstalled_GameVersionStepIsDoneWithoutAction()
    {
        var fixture = CreateServices();
        SeedInstalledVersion(fixture.FileSystem, "1.20.4");
        var viewModel = CreateViewModel(fixture);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        var versionStep = viewModel.Steps[1];
        versionStep.IsDone.ShouldBeTrue();
        versionStep.HasAction.ShouldBeFalse();
        versionStep.Subtitle.ShouldContain("1.20.4");
    }

    [Fact]
    public async Task InitializeAsync_SignedOut_AccountStepIsProposedWithoutSoundingMandatory()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        var accountStep = viewModel.Steps[2];
        accountStep.Title.ShouldBe("Compte Vintage Story");
        accountStep.IsDone.ShouldBeFalse();
        accountStep.HasAction.ShouldBeTrue();
        accountStep.ActionCommand.ShouldBeSameAs(viewModel.GoToAccountSettingsCommand);
        accountStep.Subtitle.ShouldContain("multijoueur");
    }

    [Fact]
    public async Task InitializeAsync_SignedIn_AccountStepIsDoneAndNamesThePlayer()
    {
        var fixture = CreateServices();
        await SignInAsync(fixture);
        var viewModel = CreateViewModel(fixture);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        var accountStep = viewModel.Steps[2];
        accountStep.IsDone.ShouldBeTrue();
        accountStep.HasAction.ShouldBeFalse();
        accountStep.Subtitle.ShouldContain("Sylve");
    }

    [Fact]
    public async Task GoToAccountSettingsAsync_MarksSeenClosesTheOverlayAndRaisesNavigation()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);
        fixture.Overlay.Show(viewModel);
        var navigated = false;
        viewModel.NavigateToAccountSettingsRequested += (_, _) => navigated = true;

        await viewModel.GoToAccountSettingsCommand.ExecuteAsync(null);

        fixture.Settings.Current.HasSeenFirstRun.ShouldBeTrue();
        fixture.Overlay.Active.ShouldBeNull();
        navigated.ShouldBeTrue();
    }

    [Fact]
    public async Task InitializeAsync_NothingDetected_StepsHasNoVslEntry()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        viewModel.Steps.Count.ShouldBe(3);
    }

    [Fact]
    public async Task InitializeAsync_VslDetected_AddsAFourthEntryWithAdoptAction()
    {
        var fixture = CreateServices();
        WriteVslConfigWithOneInstallation(fixture.FileSystem);
        var viewModel = CreateViewModel(fixture);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        viewModel.Steps.Count.ShouldBe(4);
        var vslStep = viewModel.Steps[3];
        vslStep.IsDone.ShouldBeFalse();
        vslStep.HasAction.ShouldBeTrue();
        vslStep.ActionCommand.ShouldBeSameAs(viewModel.OpenVslAdoptionCommand);
        vslStep.Subtitle.ShouldContain("1 installation");
    }

    [Fact]
    public async Task StartAsync_MarksSeenAndClosesTheOverlay()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);
        fixture.Overlay.Show(viewModel);

        await viewModel.StartCommand.ExecuteAsync(null);

        fixture.Settings.Current.HasSeenFirstRun.ShouldBeTrue();
        fixture.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public async Task SkipAsync_MarksSeenAndClosesTheOverlay()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);
        fixture.Overlay.Show(viewModel);

        await viewModel.SkipCommand.ExecuteAsync(null);

        fixture.Settings.Current.HasSeenFirstRun.ShouldBeTrue();
        fixture.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public async Task GoToVersionsAsync_MarksSeenClosesTheOverlayAndRaisesNavigation()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);
        fixture.Overlay.Show(viewModel);
        var navigated = false;
        viewModel.NavigateToVersionsRequested += (_, _) => navigated = true;

        await viewModel.GoToVersionsCommand.ExecuteAsync(null);

        fixture.Settings.Current.HasSeenFirstRun.ShouldBeTrue();
        fixture.Overlay.Active.ShouldBeNull();
        navigated.ShouldBeTrue();
    }

    [Fact]
    public async Task OpenVslAdoptionAsync_BeforeInitialize_DoesNothing()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);

        await viewModel.OpenVslAdoptionCommand.ExecuteAsync(null);

        fixture.Overlay.Active.ShouldBeNull();
        fixture.Settings.Current.HasSeenFirstRun.ShouldBeFalse();
    }

    [Fact]
    public async Task OpenVslAdoptionAsync_AfterDetection_MarksSeenAndShowsTheSharedAdoptionDialog()
    {
        var fixture = CreateServices();
        WriteVslConfigWithOneInstallation(fixture.FileSystem);
        var viewModel = CreateViewModel(fixture);
        await viewModel.InitializeCommand.ExecuteAsync(null);

        await viewModel.OpenVslAdoptionCommand.ExecuteAsync(null);

        fixture.Settings.Current.HasSeenFirstRun.ShouldBeTrue();
        fixture.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();
    }

    [Fact]
    public async Task OpenVslAdoptionAsync_AdoptionCompleted_BubblesUpAsVslAdopted()
    {
        var fixture = CreateServices();
        WriteVslConfigWithOneInstallation(fixture.FileSystem);
        fixture.FileSystem.AddFile(fixture.FileSystem.Path.Combine("/vsl/installations/a", "Mods", "x.zip"), new MockFileData("x"));
        var viewModel = CreateViewModel(fixture);
        await viewModel.InitializeCommand.ExecuteAsync(null);
        await viewModel.OpenVslAdoptionCommand.ExecuteAsync(null);
        var adopt = fixture.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();
        VslAdoptionOutcome? bubbled = null;
        viewModel.VslAdopted += (_, outcome) => bubbled = outcome;

        await adopt.ConfirmCommand.ExecuteAsync(null);

        bubbled.ShouldNotBeNull();
    }
}