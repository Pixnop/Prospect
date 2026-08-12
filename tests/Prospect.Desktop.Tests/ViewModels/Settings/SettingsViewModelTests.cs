using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Http;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Launching;
using Prospect.Core.Migration;
using Prospect.Core.ModDb;
using Prospect.Core.Modpacks;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.FirstRun;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Migration;
using Prospect.Desktop.ViewModels.Settings;
using Prospect.Desktop.ViewModels.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Settings;

/// <summary>
/// <see cref="SettingsViewModel"/> : détection au chemin par défaut, choix manuel d'un dossier,
/// ouverture du dialogue d'adoption partagé, rafraîchissement de l'Accueil après une adoption.
/// </summary>
public sealed class SettingsViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly string DefaultVslRoot = Path.Combine("/home/test", ".config");

    private sealed record Fixture(
        MockFileSystem FileSystem,
        FakeAppEnvironment Environment,
        RecordingOverlayService Overlay,
        FakeFilePickerService FilePicker,
        HomeViewModel Home,
        VslAdoptionService AdoptionService,
        VslDetector Detector);

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
        var detector = new VslDetector(fileSystem, environment);

        var overlay = new RecordingOverlayService();
        var filePicker = new FakeFilePickerService();
        var (launcher, tracker) = CreateLaunching(fileSystem, instanceRepository, instanceService, clock);
        var firstRunAdoptFactory = MakeAdoptFactory(fileSystem, adoptionService, gameVersions, overlay);
        var firstRun = new FirstRunViewModel(detector, firstRunAdoptFactory, overlay);
        var home = new HomeViewModel(
            instanceService,
            instanceRepository,
            launcher,
            tracker,
            clock,
            new ModUpdateCheckCache(),
            overlay,
            new RecordingToastService(),
            new ImmediateUiDispatcher(),
            filePicker,
            () => throw new InvalidOperationException("Non exercé par ces tests."),
            _ => throw new InvalidOperationException("Non exercé par ces tests."),
            firstRun);

        return new Fixture(fileSystem, environment, overlay, filePicker, home, adoptionService, detector);
    }

    private static Func<VslDetectionResult, AdoptVslViewModel> MakeAdoptFactory(
        MockFileSystem fileSystem, VslAdoptionService adoptionService, IInstalledGameVersionRepository gameVersions, RecordingOverlayService overlay)
        => detection => new AdoptVslViewModel(
            detection,
            adoptionService,
            gameVersions,
            fileSystem,
            overlay,
            new RecordingToastService(),
            new ImmediateUiDispatcher());

    private static (GameLauncher Launcher, RunningInstanceTracker Tracker) CreateLaunching(
        MockFileSystem fileSystem, IInstanceRepository repository, InstanceService service, IClock clock)
    {
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var tracker = new RunningInstanceTracker(service, clock);
        var launcher = new GameLauncher(
            repository,
            versions,
            new FakeDotnetLocator(),
            tracker,
            new LinuxGameLaunchStrategy(fileSystem),
            new FakeProcessRunner(),
            fileSystem,
            Paths,
            clock);

        return (launcher, tracker);
    }

    private static SettingsViewModel CreateViewModel(Fixture fixture)
        => new(fixture.Detector, MakeAdoptFactory(fixture.FileSystem, fixture.AdoptionService, new FileSystemInstalledGameVersionRepository(fixture.FileSystem, Paths), fixture.Overlay), fixture.Overlay, fixture.FilePicker, fixture.Home);

    private static void WriteConfigWithOneInstallation(MockFileSystem fileSystem, string root)
    {
        var path = fileSystem.Path.Combine(root, "VSLauncher", "config.json");
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
        Func<VslDetectionResult, AdoptVslViewModel> adoptFactory = _ => null!;

        Should.Throw<ArgumentNullException>(() => new SettingsViewModel(null!, adoptFactory, fixture.Overlay, fixture.FilePicker, fixture.Home));
        Should.Throw<ArgumentNullException>(() => new SettingsViewModel(fixture.Detector, null!, fixture.Overlay, fixture.FilePicker, fixture.Home));
        Should.Throw<ArgumentNullException>(() => new SettingsViewModel(fixture.Detector, adoptFactory, null!, fixture.FilePicker, fixture.Home));
        Should.Throw<ArgumentNullException>(() => new SettingsViewModel(fixture.Detector, adoptFactory, fixture.Overlay, null!, fixture.Home));
        Should.Throw<ArgumentNullException>(() => new SettingsViewModel(fixture.Detector, adoptFactory, fixture.Overlay, fixture.FilePicker, null!));
    }

    [Fact]
    public async Task InitializeAsync_NothingAtDefaultPath_VslDetectedStaysFalseWithMessage()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        viewModel.VslDetected.ShouldBeFalse();
        viewModel.VslStatusText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task InitializeAsync_InstallationAtDefaultPath_VslDetectedBecomesTrue()
    {
        var fixture = CreateServices();
        WriteConfigWithOneInstallation(fixture.FileSystem, DefaultVslRoot);
        var viewModel = CreateViewModel(fixture);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        viewModel.VslDetected.ShouldBeTrue();
    }

    [Fact]
    public async Task OpenAdoption_WhenNothingDetected_CannotExecute()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);
        await viewModel.InitializeCommand.ExecuteAsync(null);

        viewModel.OpenAdoptionCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task OpenAdoption_AfterDetection_ShowsTheAdoptionDialog()
    {
        var fixture = CreateServices();
        WriteConfigWithOneInstallation(fixture.FileSystem, DefaultVslRoot);
        var viewModel = CreateViewModel(fixture);
        await viewModel.InitializeCommand.ExecuteAsync(null);

        viewModel.OpenAdoptionCommand.Execute(null);

        fixture.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();
    }

    [Fact]
    public async Task PickFolderAsync_UserCancels_LeavesDetectionUnchanged()
    {
        var fixture = CreateServices();
        fixture.FilePicker.NextFolderPath = null;
        var viewModel = CreateViewModel(fixture);
        await viewModel.InitializeCommand.ExecuteAsync(null);

        await viewModel.PickFolderCommand.ExecuteAsync(null);

        viewModel.VslDetected.ShouldBeFalse();
    }

    [Fact]
    public async Task PickFolderAsync_CustomFolderWithInstallations_DetectsThere()
    {
        var fixture = CreateServices();
        const string customRoot = "/mnt/backup/vsl-portable";
        WriteConfigWithOneInstallation(fixture.FileSystem, customRoot);
        fixture.FilePicker.NextFolderPath = customRoot;
        var viewModel = CreateViewModel(fixture);
        await viewModel.InitializeCommand.ExecuteAsync(null);
        viewModel.VslDetected.ShouldBeFalse();

        await viewModel.PickFolderCommand.ExecuteAsync(null);

        viewModel.VslDetected.ShouldBeTrue();
    }

    [Fact]
    public async Task PickFolderAsync_CustomFolderWithNothing_ReportsNotDetected()
    {
        var fixture = CreateServices();
        fixture.FilePicker.NextFolderPath = "/mnt/backup/empty";
        var viewModel = CreateViewModel(fixture);

        await viewModel.PickFolderCommand.ExecuteAsync(null);

        viewModel.VslDetected.ShouldBeFalse();
        viewModel.VslStatusText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task AdoptionCompleted_RefreshesHome()
    {
        var fixture = CreateServices();
        WriteConfigWithOneInstallation(fixture.FileSystem, DefaultVslRoot);
        fixture.FileSystem.AddFile(fixture.FileSystem.Path.Combine("/vsl/installations/a", "Mods", "x.zip"), new MockFileData("x"));
        var viewModel = CreateViewModel(fixture);
        await viewModel.InitializeCommand.ExecuteAsync(null);
        viewModel.OpenAdoptionCommand.Execute(null);
        var adopt = fixture.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();

        await adopt.ConfirmCommand.ExecuteAsync(null);

        fixture.Home.Instances.ShouldContain(instance => instance.Name == "Survie");
    }
}