using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Backups;
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
using Prospect.Desktop.ViewModels.Modpacks;
using Prospect.Desktop.ViewModels.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Home;

/// <summary>
/// Tests unitaires purs (aucun <c>[AvaloniaFact]</c>) de la logique de tri/filtre de
/// <see cref="HomeViewModel"/> : <see cref="InstanceService"/> et <see cref="IInstanceRepository"/>
/// réels sur <see cref="MockFileSystem"/>, services Desktop transverses en doubles de test. Le
/// lancement (<see cref="GameLauncher"/>/<see cref="RunningInstanceTracker"/>) est construit réel
/// lui aussi (mêmes collaborateurs que <see cref="Prospect.Desktop.CompositionRoot"/>) : ces tests
/// ne l'exercent pas directement (voir InstanceCardViewModelTests pour Jouer/Arrêter), mais
/// HomeViewModel en a besoin pour construire ses cartes.
/// </summary>
public class HomeViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    private static (HomeViewModel ViewModel, InstanceService Service, IInstanceRepository Repository) CreateViewModel()
    {
        var (viewModel, service, repository, _, _) = CreateViewModelWithDoubles();

        return (viewModel, service, repository);
    }

    private static (
        HomeViewModel ViewModel,
        InstanceService Service,
        IInstanceRepository Repository,
        FakeFilePickerService FilePicker,
        MockFileSystem FileSystem) CreateViewModelWithDoubles()
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var clock = new FakeClock(Now);
        var service = new InstanceService(repository, fileSystem, clock);
        var overlay = new RecordingOverlayService();
        var filePicker = new FakeFilePickerService();
        var (launcher, tracker) = CreateLaunching(fileSystem, repository, service, clock);
        var viewModel = new HomeViewModel(
            service,
            repository,
            launcher,
            tracker,
            clock,
            new ModUpdateCheckCache(),
            overlay,
            new RecordingToastService(),
            new ImmediateUiDispatcher(),
            filePicker,
            WizardFactory(service, overlay, fileSystem),
            ImportFactory(fileSystem, repository, service, overlay, clock),
            FirstRunFactory(fileSystem, overlay, service, repository, clock));
        return (viewModel, service, repository, filePicker, fileSystem);
    }

    // Rien de spécifique à VS Launcher n'est exercé par les tests de ce fichier (tri/filtre de la
    // grille) : un FirstRunViewModel réel, construit comme le ferait CompositionRoot, suffit à
    // satisfaire le constructeur de HomeViewModel.
    private static FirstRunViewModel FirstRunFactory(
        MockFileSystem fileSystem, IOverlayService overlay, InstanceService service, IInstanceRepository repository, IClock clock)
    {
        var detector = new VslDetector(fileSystem, new SystemAppEnvironment());
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var adoptionService = new VslAdoptionService(service, repository, versions, new JsonFileStore(fileSystem), fileSystem, clock);
        Func<VslDetectionResult, AdoptVslViewModel> adoptFactory = detection => new AdoptVslViewModel(
            detection,
            adoptionService,
            versions,
            fileSystem,
            overlay,
            new RecordingToastService(),
            new ImmediateUiDispatcher());

        return new FirstRunViewModel(detector, adoptFactory, overlay);
    }

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
            clock,
            AccountDoubles.SignedOut(),
            AccountDoubles.ClientSettings(fileSystem),
            new InstanceBackupService(repository, fileSystem, clock));

        return (launcher, tracker);
    }

    // Le wizard a ses propres dépendances (catalogue, installations) depuis qu'il porte le
    // sélecteur de versions ; l'Accueil ne les connaît pas, il reçoit une fabrique.
    private static Func<WizardViewModel> WizardFactory(InstanceService service, IOverlayService overlay, MockFileSystem fileSystem)
    {
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var catalog = new FakeGameVersionCatalog { Catalog = FakeGameVersionCatalog.Build("1.21.3") };
        var installService = new GameInstallService(catalog, new FakeDownloadManager(), versions, new FakeGameInstallStrategy(fileSystem), fileSystem, NullAppLog.Instance);

        return () => new WizardViewModel(service, overlay, catalog, versions, installService, new ImmediateUiDispatcher(), new FakeAppEnvironment());
    }

    // Même principe que WizardFactory : l'import compose autant de services que le domaine
    // Modpacks en réutilise (ModDb, versions du jeu, mods), l'Accueil ne les porte pas lui-même.
    private static Func<string, ImportModpackViewModel> ImportFactory(
        MockFileSystem fileSystem, IInstanceRepository repository, InstanceService service, IOverlayService overlay, IClock clock)
    {
        var mods = ModDbDoubles.CreateRepository(fileSystem, repository, Paths);
        var modDbClient = ModDbDoubles.CreateClient(fileSystem, Paths, clock);
        var downloads = new FakeDownloadManager();
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var catalog = new FakeGameVersionCatalog { Catalog = FakeGameVersionCatalog.Build("1.21.3") };
        var gameInstall = new GameInstallService(catalog, downloads, versions, new FakeGameInstallStrategy(fileSystem), fileSystem, NullAppLog.Instance);
        var importService = new ModpackImportService(modDbClient, downloads, mods, service, repository, gameInstall, versions, catalog, fileSystem, clock);

        return sourcePath => new ImportModpackViewModel(sourcePath, importService, overlay, new RecordingToastService(), new ImmediateUiDispatcher());
    }

    [Fact]
    public async Task RefreshAsync_NoInstances_HasNoInstancesAtAllAndEmptyGrid()
    {
        var (viewModel, _, _) = CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.HasNoInstancesAtAll.ShouldBeTrue();
        viewModel.ShowSearchEmptyState.ShouldBeFalse();
        viewModel.Instances.ShouldBeEmpty();
        viewModel.GridItems.ShouldBeEmpty();
    }

    [Fact]
    public async Task RefreshAsync_WithInstances_GridItemsEndsWithNewInstanceTile()
    {
        var (viewModel, service, _) = CreateViewModel();
        await service.CreateAsync("Homestead", GameVersion.Parse("1.21.3"));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.Instances.Count.ShouldBe(1);
        viewModel.GridItems.Count.ShouldBe(2);
        viewModel.GridItems[^1].ShouldBeOfType<NewInstanceTileViewModel>();
        viewModel.HasNoInstancesAtAll.ShouldBeFalse();
    }

    [Fact]
    public async Task SearchText_FiltersByNameCaseInsensitive()
    {
        var (viewModel, service, _) = CreateViewModel();
        await service.CreateAsync("Vintage Survival", GameVersion.Parse("1.20.4"));
        await service.CreateAsync("Technocracy", GameVersion.Parse("1.21.0-rc.2"));
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SearchText = "VINTAGE";

        viewModel.Instances.Count.ShouldBe(1);
        viewModel.Instances[0].Name.ShouldBe("Vintage Survival");
    }

    [Fact]
    public async Task SearchText_NoMatch_ShowsSearchEmptyStateNotFirstRunEmptyState()
    {
        var (viewModel, service, _) = CreateViewModel();
        await service.CreateAsync("Vintage Survival", GameVersion.Parse("1.20.4"));
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SearchText = "does-not-exist";

        viewModel.ShowSearchEmptyState.ShouldBeTrue();
        viewModel.HasNoInstancesAtAll.ShouldBeFalse();
        viewModel.Instances.ShouldBeEmpty();
        viewModel.GridItems.ShouldBeEmpty();
    }

    [Fact]
    public async Task ClearSearch_ResetsFilterAndRestoresFullList()
    {
        var (viewModel, service, _) = CreateViewModel();
        await service.CreateAsync("Vintage Survival", GameVersion.Parse("1.20.4"));
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SearchText = "nope";

        viewModel.ClearSearchCommand.Execute(null);

        viewModel.SearchText.ShouldBe(string.Empty);
        viewModel.Instances.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SortMode_Name_OrdersAlphabetically()
    {
        var (viewModel, service, _) = CreateViewModel();
        await service.CreateAsync("Zebra", GameVersion.Parse("1.20.4"));
        await service.CreateAsync("Alpha", GameVersion.Parse("1.20.4"));
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SortMode = HomeSortMode.Name;

        viewModel.Instances.Select(instance => instance.Name).ShouldBe(["Alpha", "Zebra"]);
    }

    [Fact]
    public async Task SortMode_LastLaunched_OrdersMostRecentFirst()
    {
        var (viewModel, service, repository) = CreateViewModel();
        var older = await service.CreateAsync("Older", GameVersion.Parse("1.20.4"));
        var newer = await service.CreateAsync("Newer", GameVersion.Parse("1.20.4"));
        await repository.SaveAsync(older with { Metadata = older.Metadata with { LastLaunchedUtc = Now.AddDays(-5) } });
        await repository.SaveAsync(newer with { Metadata = newer.Metadata with { LastLaunchedUtc = Now.AddDays(-1) } });

        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SortMode.ShouldBe(HomeSortMode.LastLaunched);
        viewModel.Instances.Select(instance => instance.Name).ShouldBe(["Newer", "Older"]);
    }

    [Fact]
    public async Task SortMode_LastLaunched_NeverLaunchedSortsAfterLaunched()
    {
        var (viewModel, service, repository) = CreateViewModel();
        var launched = await service.CreateAsync("Launched", GameVersion.Parse("1.20.4"));
        await service.CreateAsync("NeverLaunched", GameVersion.Parse("1.20.4"));
        await repository.SaveAsync(launched with { Metadata = launched.Metadata with { LastLaunchedUtc = Now.AddDays(-1) } });

        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.Instances.Select(instance => instance.Name).ShouldBe(["Launched", "NeverLaunched"]);
    }

    [Fact]
    public async Task RefreshAsync_BrokenInstanceFolder_PopulatesBrokenInstancesWithoutFailing()
    {
        var (viewModel, service, _) = CreateViewModel();
        await service.CreateAsync("Homestead", GameVersion.Parse("1.21.3"));
        var fileSystem = new MockFileSystem();
        // Un second scan sur un dossier "instances" contenant un dossier sans instance.json :
        // recréé directement pour ne pas dépendre de l'implémentation interne du premier fileSystem.
        var brokenRepository = new FileSystemInstanceRepository(
            fileSystem,
            Paths,
            new JsonFileStore(fileSystem),
            new InstanceMetadataMigrationPipeline([]));
        fileSystem.AddDirectory(brokenRepository.GetInstanceDirectory("ghost-folder"));
        var brokenClock = new FakeClock(Now);
        var brokenService = new InstanceService(brokenRepository, fileSystem, brokenClock);
        var brokenOverlay = new RecordingOverlayService();
        var (brokenLauncher, brokenTracker) = CreateLaunching(fileSystem, brokenRepository, brokenService, brokenClock);
        var brokenViewModel = new HomeViewModel(
            brokenService,
            brokenRepository,
            brokenLauncher,
            brokenTracker,
            brokenClock,
            new ModUpdateCheckCache(),
            brokenOverlay,
            new RecordingToastService(),
            new ImmediateUiDispatcher(),
            new FakeFilePickerService(),
            WizardFactory(brokenService, brokenOverlay, fileSystem),
            ImportFactory(fileSystem, brokenRepository, brokenService, brokenOverlay, brokenClock),
            FirstRunFactory(fileSystem, brokenOverlay, brokenService, brokenRepository, brokenClock));

        await brokenViewModel.RefreshCommand.ExecuteAsync(null);

        brokenViewModel.HasBrokenInstances.ShouldBeTrue();
        brokenViewModel.BrokenInstances.Count.ShouldBe(1);
        brokenViewModel.BrokenInstances[0].FolderName.ShouldBe("ghost-folder");
    }
}