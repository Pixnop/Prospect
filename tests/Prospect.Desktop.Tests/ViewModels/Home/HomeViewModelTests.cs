using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Home;

/// <summary>
/// Tests unitaires purs (aucun <c>[AvaloniaFact]</c>) de la logique de tri/filtre de
/// <see cref="HomeViewModel"/> : <see cref="InstanceService"/> et <see cref="IInstanceRepository"/>
/// réels sur <see cref="MockFileSystem"/>, services Desktop transverses en doubles de test.
/// </summary>
public class HomeViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    private static (HomeViewModel ViewModel, InstanceService Service, IInstanceRepository Repository) CreateViewModel()
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var clock = new FakeClock(Now);
        var service = new InstanceService(repository, fileSystem, clock);
        var overlay = new RecordingOverlayService();
        var viewModel = new HomeViewModel(service, repository, clock, overlay, new RecordingToastService(), WizardFactory(service, overlay, fileSystem));
        return (viewModel, service, repository);
    }

    // Le wizard a ses propres dépendances (catalogue, installations) depuis qu'il porte le
    // sélecteur de versions ; l'Accueil ne les connaît pas, il reçoit une fabrique.
    private static Func<WizardViewModel> WizardFactory(InstanceService service, IOverlayService overlay, MockFileSystem fileSystem)
    {
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var catalog = new FakeGameVersionCatalog { Catalog = FakeGameVersionCatalog.Build("1.21.3") };
        var installService = new GameInstallService(catalog, new FakeDownloadManager(), versions, new FakeGameInstallStrategy());

        return () => new WizardViewModel(service, overlay, catalog, versions, installService, new ImmediateUiDispatcher());
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
        var brokenService = new InstanceService(brokenRepository, fileSystem, new FakeClock(Now));
        var brokenOverlay = new RecordingOverlayService();
        var brokenViewModel = new HomeViewModel(
            brokenService,
            brokenRepository,
            new FakeClock(Now),
            brokenOverlay,
            new RecordingToastService(),
            WizardFactory(brokenService, brokenOverlay, fileSystem));

        await brokenViewModel.RefreshCommand.ExecuteAsync(null);

        brokenViewModel.HasBrokenInstances.ShouldBeTrue();
        brokenViewModel.BrokenInstances.Count.ShouldBe(1);
        brokenViewModel.BrokenInstances[0].FolderName.ShouldBe("ghost-folder");
    }
}