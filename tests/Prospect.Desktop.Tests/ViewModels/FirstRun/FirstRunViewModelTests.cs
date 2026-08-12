using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Migration;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.FirstRun;
using Prospect.Desktop.ViewModels.Migration;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.FirstRun;

/// <summary>
/// <see cref="FirstRunViewModel"/> : détection, carte conditionnelle, ouverture du dialogue
/// d'adoption partagé avec les Réglages.
/// </summary>
public sealed class FirstRunViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(MockFileSystem FileSystem, FakeAppEnvironment Environment, RecordingOverlayService Overlay, VslAdoptionService AdoptionService);

    // FakeAppEnvironment (Desktop) ne permet pas de poser de variable d'environnement (contrairement
    // à celle de Prospect.Core.Tests) : GetEnvironmentVariable rend toujours null, donc VslPaths
    // retombe systématiquement sur son repli Linux, Path.Combine(GetFolderPath(UserProfile), ".config").
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

        return new Fixture(fileSystem, environment, new RecordingOverlayService(), adoptionService);
    }

    private static FirstRunViewModel CreateViewModel(Fixture fixture)
    {
        var detector = new VslDetector(fixture.FileSystem, fixture.Environment);
        var gameVersions = new FileSystemInstalledGameVersionRepository(fixture.FileSystem, Paths);
        Func<VslDetectionResult, AdoptVslViewModel> adoptFactory = detection => new AdoptVslViewModel(
            detection,
            fixture.AdoptionService,
            gameVersions,
            fixture.FileSystem,
            fixture.Overlay,
            new RecordingToastService(),
            new ImmediateUiDispatcher());

        return new FirstRunViewModel(detector, adoptFactory, fixture.Overlay);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fixture = CreateServices();
        var detector = new VslDetector(fixture.FileSystem, fixture.Environment);
        Func<VslDetectionResult, AdoptVslViewModel> adoptFactory = _ => null!;

        Should.Throw<ArgumentNullException>(() => new FirstRunViewModel(null!, adoptFactory, fixture.Overlay));
        Should.Throw<ArgumentNullException>(() => new FirstRunViewModel(detector, null!, fixture.Overlay));
        Should.Throw<ArgumentNullException>(() => new FirstRunViewModel(detector, adoptFactory, null!));
    }

    [Fact]
    public async Task InitializeAsync_NothingOnDisk_VslDetectedStaysFalse()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        viewModel.VslDetected.ShouldBeFalse();
    }

    [Fact]
    public async Task InitializeAsync_InstallationDetected_VslDetectedBecomesTrueWithSummary()
    {
        var fixture = CreateServices();
        WriteConfigWithOneInstallation(fixture.FileSystem);
        var viewModel = CreateViewModel(fixture);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        viewModel.VslDetected.ShouldBeTrue();
        viewModel.VslSummaryText.ShouldContain("1 installation");
    }

    [Fact]
    public void OpenAdoption_BeforeInitialize_DoesNothing()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture);

        viewModel.OpenAdoptionCommand.Execute(null);

        fixture.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public async Task OpenAdoption_AfterDetection_ShowsTheAdoptionDialog()
    {
        var fixture = CreateServices();
        WriteConfigWithOneInstallation(fixture.FileSystem);
        var viewModel = CreateViewModel(fixture);
        await viewModel.InitializeCommand.ExecuteAsync(null);

        viewModel.OpenAdoptionCommand.Execute(null);

        fixture.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();
    }

    [Fact]
    public async Task AdoptionCompleted_BubblesUpAsFirstRunCompleted()
    {
        var fixture = CreateServices();
        WriteConfigWithOneInstallation(fixture.FileSystem);
        fixture.FileSystem.AddFile(fixture.FileSystem.Path.Combine("/vsl/installations/a", "Mods", "x.zip"), new MockFileData("x"));
        var viewModel = CreateViewModel(fixture);
        await viewModel.InitializeCommand.ExecuteAsync(null);
        viewModel.OpenAdoptionCommand.Execute(null);
        var adopt = fixture.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();
        VslAdoptionOutcome? bubbled = null;
        viewModel.Completed += (_, outcome) => bubbled = outcome;

        await adopt.ConfirmCommand.ExecuteAsync(null);

        bubbled.ShouldNotBeNull();
    }

    private static void WriteConfigWithOneInstallation(MockFileSystem fileSystem)
    {
        var path = fileSystem.Path.Combine(VslRoot, "VSLauncher", "config.json");
        fileSystem.AddFile(path, new MockFileData("""
        {
          "installations": [ { "id": "a", "name": "Survie", "path": "/vsl/installations/a", "version": "1.20.4" } ],
          "gameVersions": []
        }
        """));
    }
}