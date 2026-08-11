using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Versions;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Versions;

/// <summary>
/// Tests unitaires purs de <see cref="VersionsViewModel"/> : répartition installées/disponibles,
/// filtres de la maquette, désinstallation qui nomme les instances concernées, installation qui
/// passe bien par <see cref="GameInstallService"/>.
/// </summary>
public class VersionsViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        VersionsViewModel ViewModel,
        MockFileSystem FileSystem,
        FakeGameVersionCatalog Catalog,
        FakeGameInstallStrategy Strategy,
        FakeDownloadManager Downloads,
        IInstalledGameVersionRepository Versions,
        InstanceService Instances,
        RecordingOverlayService Overlay,
        RecordingToastService Toasts);

    private static Harness CreateHarness(params string[] installedVersions)
    {
        var fileSystem = new MockFileSystem();
        var instanceRepository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var instances = new InstanceService(instanceRepository, fileSystem, new FakeClock(Now));
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);

        foreach (var version in installedVersions)
        {
            var directory = fileSystem.Path.Combine(Paths.VersionsDirectory, version);
            fileSystem.AddFile(fileSystem.Path.Combine(directory, "Vintagestory"), new MockFileData(new byte[1024]));
            fileSystem.AddFile(
                fileSystem.Path.Combine(directory, FileSystemInstalledGameVersionRepository.CompletionMarkerFileName),
                new MockFileData(version));
        }

        var catalog = new FakeGameVersionCatalog { Catalog = FakeGameVersionCatalog.Build("1.22.0-rc.1", "1.21.3", "1.20.4") };
        var downloads = new FakeDownloadManager();
        var strategy = new FakeGameInstallStrategy();
        var installService = new GameInstallService(catalog, downloads, versions, strategy);
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();
        var viewModel = new VersionsViewModel(
            catalog,
            versions,
            installService,
            instanceRepository,
            Paths,
            overlay,
            toasts,
            new ImmediateUiDispatcher());

        return new Harness(viewModel, fileSystem, catalog, strategy, downloads, versions, instances, overlay, toasts);
    }

    private static async Task<Harness> CreateLoadedHarnessAsync(params string[] installedVersions)
    {
        var harness = CreateHarness(installedVersions);
        await harness.ViewModel.RefreshCommand.ExecuteAsync(null);

        return harness;
    }

    [Fact]
    public async Task Refresh_SplitsInstalledFromAvailable()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");

        harness.ViewModel.Installed.Select(row => row.VersionText).ShouldBe(["1.20.4"]);
        harness.ViewModel.Available.Select(row => row.VersionText).ShouldBe(["1.22.0-rc.1", "1.21.3"]);
        harness.ViewModel.ShowInstalledSection.ShouldBeTrue();
        harness.ViewModel.ShowAvailableSection.ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_SubtitleCountsInstallsAndTheirSizeOnDisk()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4", "1.21.3");

        harness.ViewModel.SubtitleText.ShouldStartWith("2 installées · ");
        harness.ViewModel.SubtitleText.ShouldContain("KB");
    }

    [Fact]
    public async Task Refresh_NothingInstalled_SaysSoInTheSubtitle()
    {
        var harness = await CreateLoadedHarnessAsync();

        harness.ViewModel.SubtitleText.ShouldStartWith("Aucune version installée");
        harness.ViewModel.ShowInstalledSection.ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_FolderWithoutSentinel_IsListedAsBroken()
    {
        var harness = CreateHarness();
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(Paths.VersionsDirectory, "1.19.8", "Vintagestory"),
            new MockFileData("interrompu"));

        await harness.ViewModel.RefreshCommand.ExecuteAsync(null);

        harness.ViewModel.HasBrokenInstalls.ShouldBeTrue();
        harness.ViewModel.BrokenInstalls.ShouldHaveSingleItem().FolderName.ShouldBe("1.19.8");
        harness.ViewModel.BrokenInstalls[0].Reason.ShouldContain("interrompue");
    }

    [Fact]
    public async Task ShowUnstable_Unchecked_HidesReleaseCandidates()
    {
        var harness = await CreateLoadedHarnessAsync();

        harness.ViewModel.ShowUnstable = false;

        harness.ViewModel.Available.Select(row => row.VersionText).ShouldBe(["1.21.3", "1.20.4"]);
    }

    [Fact]
    public async Task Filter_Installed_HidesTheAvailableSection()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");

        harness.ViewModel.FilterIndex = 1;

        harness.ViewModel.Available.ShouldBeEmpty();
        harness.ViewModel.ShowAvailableSection.ShouldBeFalse();
        harness.ViewModel.Installed.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Filter_Available_HidesTheInstalledSection()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");

        harness.ViewModel.FilterIndex = 2;

        harness.ViewModel.Installed.ShouldBeEmpty();
        harness.ViewModel.ShowInstalledSection.ShouldBeFalse();
    }

    [Fact]
    public async Task Search_NarrowsBothSections()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");

        harness.ViewModel.SearchText = "1.22";

        harness.ViewModel.Installed.ShouldBeEmpty();
        harness.ViewModel.Available.Select(row => row.VersionText).ShouldBe(["1.22.0-rc.1"]);
    }

    [Fact]
    public async Task Search_NoMatch_ShowsTheEmptyState()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");

        harness.ViewModel.SearchText = "9.9.9";

        harness.ViewModel.ShowEmptyState.ShouldBeTrue();

        harness.ViewModel.ClearSearchCommand.Execute(null);

        harness.ViewModel.ShowEmptyState.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckForUpdates_AsksTheCatalogToIgnoreItsCache()
    {
        var harness = await CreateLoadedHarnessAsync();

        await harness.ViewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        harness.Catalog.LastForceRefresh.ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_CatalogUnreachable_KeepsInstalledVersionsAndWarns()
    {
        var harness = CreateHarness("1.20.4");
        harness.Catalog.IsUnavailable = true;

        await harness.ViewModel.RefreshCommand.ExecuteAsync(null);

        harness.ViewModel.Installed.Count.ShouldBe(1);
        harness.ViewModel.Available.ShouldBeEmpty();
        harness.ViewModel.CatalogWarning.ShouldNotBeNull();
    }

    [Fact]
    public async Task Refresh_StaleCatalog_SaysTheDataIsNotFresh()
    {
        var harness = CreateHarness();
        var fresh = FakeGameVersionCatalog.Build("1.21.3");
        harness.Catalog.Catalog = new GameVersionCatalog(fresh.Versions, Now, GameCatalogFreshness.Stale);

        await harness.ViewModel.RefreshCommand.ExecuteAsync(null);

        harness.ViewModel.CatalogWarning.ShouldNotBeNull();
        harness.ViewModel.Available.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Install_GoesThroughTheInstallServiceAndMovesTheRowToTheInstalledSection()
    {
        var harness = await CreateLoadedHarnessAsync();
        var row = harness.ViewModel.Available.First(candidate => candidate.VersionText == "1.21.3");

        await row.InstallCommand.ExecuteAsync(null);

        harness.Downloads.Requests.ShouldHaveSingleItem();
        harness.Strategy.Installs.ShouldHaveSingleItem();
        harness.Versions.IsInstalled(GameVersion.Parse("1.21.3")).ShouldBeTrue();
        harness.ViewModel.Installed.Select(candidate => candidate.VersionText).ShouldContain("1.21.3");
        harness.Toasts.Shown.ShouldContain(toast => toast.Title == "Version installée");
    }

    [Fact]
    public async Task Install_Fails_ShowsTheReasonOnTheRow()
    {
        var harness = await CreateLoadedHarnessAsync();
        harness.Strategy.Failure = new GameInstallFailedException("archive illisible");
        var row = harness.ViewModel.Available.First(candidate => candidate.VersionText == "1.21.3");

        await row.InstallCommand.ExecuteAsync(null);

        row.ErrorMessage.ShouldBe("archive illisible");
        row.IsWorking.ShouldBeFalse();
        harness.Versions.IsInstalled(GameVersion.Parse("1.21.3")).ShouldBeFalse();
    }

    [Fact]
    public async Task Uninstall_WithoutAnyInstanceUsingIt_AsksForPlainConfirmation()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");

        await harness.ViewModel.Installed[0].UninstallCommand.ExecuteAsync(null);

        var dialog = harness.Overlay.Active.ShouldBeOfType<UninstallVersionDialogViewModel>();
        dialog.HasDependents.ShouldBeFalse();
        dialog.DependentsMessage.ShouldBeNull();
        dialog.Title.ShouldContain("1.20.4");
    }

    [Fact]
    public async Task Uninstall_WithInstancesUsingIt_NamesThemInTheWarning()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");
        await harness.Instances.CreateAsync("Homestead", GameVersion.Parse("1.20.4"), CancellationToken.None);
        await harness.Instances.CreateAsync("Bac à sable", GameVersion.Parse("1.20.4"), CancellationToken.None);
        await harness.Instances.CreateAsync("Autre", GameVersion.Parse("1.21.3"), CancellationToken.None);

        await harness.ViewModel.Installed[0].UninstallCommand.ExecuteAsync(null);

        var dialog = harness.Overlay.Active.ShouldBeOfType<UninstallVersionDialogViewModel>();
        dialog.HasDependents.ShouldBeTrue();
        var dependents = dialog.DependentsMessage.ShouldNotBeNull();
        dependents.ShouldContain("« Bac à sable »");
        dependents.ShouldContain("« Homestead »");
        dependents.ShouldNotContain("Autre");
    }

    [Fact]
    public async Task Uninstall_Confirmed_RemovesTheVersionAndRefreshes()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");
        await harness.ViewModel.Installed[0].UninstallCommand.ExecuteAsync(null);
        var dialog = harness.Overlay.Active.ShouldBeOfType<UninstallVersionDialogViewModel>();

        await dialog.ConfirmCommand.ExecuteAsync(null);

        harness.Versions.IsInstalled(GameVersion.Parse("1.20.4")).ShouldBeFalse();
        harness.ViewModel.Installed.ShouldBeEmpty();
        harness.Overlay.Active.ShouldBeNull();
        harness.Toasts.Shown.ShouldContain(toast => toast.Title == "Version désinstallée");
    }

    [Fact]
    public void VersionsDirectoryText_PointsAtTheSharedFolder()
        => CreateHarness().ViewModel.VersionsDirectoryText.ShouldBe(Paths.VersionsDirectory);

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var harness = CreateHarness();
        var fileSystem = new MockFileSystem();
        var instanceRepository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var install = new GameInstallService(harness.Catalog, harness.Downloads, versions, harness.Strategy);
        var dispatcher = new ImmediateUiDispatcher();

        Should.Throw<ArgumentNullException>(() => new VersionsViewModel(null!, versions, install, instanceRepository, Paths, harness.Overlay, harness.Toasts, dispatcher));
        Should.Throw<ArgumentNullException>(() => new VersionsViewModel(harness.Catalog, null!, install, instanceRepository, Paths, harness.Overlay, harness.Toasts, dispatcher));
        Should.Throw<ArgumentNullException>(() => new VersionsViewModel(harness.Catalog, versions, null!, instanceRepository, Paths, harness.Overlay, harness.Toasts, dispatcher));
        Should.Throw<ArgumentNullException>(() => new VersionsViewModel(harness.Catalog, versions, install, null!, Paths, harness.Overlay, harness.Toasts, dispatcher));
        Should.Throw<ArgumentNullException>(() => new VersionsViewModel(harness.Catalog, versions, install, instanceRepository, null!, harness.Overlay, harness.Toasts, dispatcher));
        Should.Throw<ArgumentNullException>(() => new VersionsViewModel(harness.Catalog, versions, install, instanceRepository, Paths, null!, harness.Toasts, dispatcher));
        Should.Throw<ArgumentNullException>(() => new VersionsViewModel(harness.Catalog, versions, install, instanceRepository, Paths, harness.Overlay, null!, dispatcher));
        Should.Throw<ArgumentNullException>(() => new VersionsViewModel(harness.Catalog, versions, install, instanceRepository, Paths, harness.Overlay, harness.Toasts, null!));
    }
}