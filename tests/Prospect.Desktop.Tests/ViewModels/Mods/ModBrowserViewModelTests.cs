using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Toasts;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Mods;

public sealed class ModBrowserViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        ModBrowserViewModel Browser,
        FakeModDbHandler Handler,
        RecordingOverlayService Overlay,
        RecordingToastService Toasts,
        IInstalledModRepository Mods,
        MockFileSystem FileSystem,
        string Slug);

    private static async Task<Fixture> CreateAsync(string gameVersion = "1.21.3")
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var service = new InstanceService(instances, fileSystem, clock);
        var record = await service.CreateAsync("Homestead", GameVersion.Parse(gameVersion));

        var handler = new FakeModDbHandler();
        var mods = ModDbDoubles.CreateRepository(fileSystem, instances, Paths);
        var installService = ModDbDoubles.CreateInstallService(fileSystem, instances, mods, Paths, clock, handler);
        var client = ModDbDoubles.CreateClient(fileSystem, Paths, clock, handler);
        var logoCache = ModDbDoubles.CreateLogoCache(handler);
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();

        var browser = new ModBrowserViewModel(client, installService, instances, new FakeExternalUrlOpener(), overlay, toasts, logoCache);

        return new Fixture(browser, handler, overlay, toasts, mods, fileSystem, record.Slug);
    }

    [Fact]
    public async Task InitializeAsync_LoadsTheCatalogTagsAndTargetInstances()
    {
        var fixture = await CreateAsync();

        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Browser.Results.Count.ShouldBe(2);
        fixture.Browser.Tags.Select(tag => tag.Name).ShouldBe(["Exploration", "Utility", "Worldgen"]);
        fixture.Browser.TargetInstances.Count.ShouldBe(2);
        fixture.Browser.SubtitleText.ShouldContain("2 mods indexés");
    }

    [Fact]
    public async Task SearchText_FiltersInMemoryWithoutAnotherNetworkCall()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Browser.SearchText = "ruins";

        fixture.Browser.Results.ShouldHaveSingleItem().Name.ShouldBe("BetterRuins");
    }

    [Fact]
    public async Task ClearSearch_RestoresEveryResult()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);
        fixture.Browser.SearchText = "ruins";

        fixture.Browser.ClearSearchCommand.Execute(null);

        fixture.Browser.Results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ToggleTag_FiltersOnThatCategoryThenReleasesItOnASecondClick()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);
        var utility = fixture.Browser.Tags.Single(tag => tag.Name == "Utility");

        await fixture.Browser.ToggleTagCommand.ExecuteAsync(utility);

        fixture.Browser.Results.ShouldHaveSingleItem().Name.ShouldBe("Config lib");
        utility.IsActive.ShouldBeTrue();

        await fixture.Browser.ToggleTagCommand.ExecuteAsync(utility);

        fixture.Browser.Results.Count.ShouldBe(2);
        utility.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task SortIndex_ChangesTheOrderWithoutRefetching()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Browser.SortIndex = (int)ModCatalogSort.Name;

        fixture.Browser.Results.Select(card => card.Name).ShouldBe(["BetterRuins", "Config lib"]);
    }

    [Fact]
    public async Task SelectedInstance_DrivesCompatibilityBadgesAndFiltersOutIncompatibleMods()
    {
        var fixture = await CreateAsync();
        fixture.Handler.CompatibleModIds = [1783];

        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        // Une instance réelle est sélectionnée d'office, comme dans la maquette.
        fixture.Browser.SelectedInstance!.Slug.ShouldBe(fixture.Slug);
        var card = fixture.Browser.Results.ShouldHaveSingleItem();
        card.Name.ShouldBe("Config lib");
        card.ShowCompatibility.ShouldBeTrue();
        card.CompatibilityTone.ShouldBe("stable");
        card.CompatibilityText.ShouldBe("1.21.3");
    }

    [Fact]
    public async Task SelectedInstance_AllVersions_ShowsEverythingWithoutBadges()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Browser.SelectedInstance = fixture.Browser.TargetInstances[0];

        fixture.Browser.Results.Count.ShouldBe(2);
        fixture.Browser.Results.ShouldAllBe(card => !card.ShowCompatibility);
    }

    [Fact]
    public async Task PendingInstanceSlug_PreselectsThatInstanceOnLoad()
    {
        var fixture = await CreateAsync();
        fixture.Browser.PendingInstanceSlug = fixture.Slug;

        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Browser.SelectedInstance!.Slug.ShouldBe(fixture.Slug);
        fixture.Browser.PendingInstanceSlug.ShouldBeNull();
    }

    [Fact]
    public async Task InitializeAsync_ModDbUnreachable_ShowsTheOfflineBannerAndItsEmptyState()
    {
        var fixture = await CreateAsync();
        fixture.Handler.IsOnline = false;

        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Browser.IsOffline.ShouldBeTrue();
        fixture.Browser.Results.ShouldBeEmpty();
        fixture.Browser.ShowEmptyState.ShouldBeTrue();
        fixture.Browser.EmptyStateTitle.ShouldBe("Aucun résultat hors ligne");
    }

    [Fact]
    public async Task RefreshAsync_AfterComingBackOnline_ClearsTheOfflineBanner()
    {
        var fixture = await CreateAsync();
        fixture.Handler.IsOnline = false;
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Handler.IsOnline = true;
        await fixture.Browser.RefreshCommand.ExecuteAsync(null);

        fixture.Browser.IsOffline.ShouldBeFalse();
        fixture.Browser.Results.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task OpenCard_ShowsTheDetailDialog()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        await fixture.Browser.Results.Single(card => card.Name == "Config lib").OpenCommand.ExecuteAsync(null);

        var dialog = fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<ModDetailDialogViewModel>();
        dialog.Name.ShouldBe("Config lib");
        // La description ModDB arrive en HTML : elle est affichée nettoyée de son balisage.
        dialog.Description.ShouldBe("Config lib\nA universal place to configure your mods.");
        dialog.PageUrlText.ShouldBe("https://mods.vintagestory.at/show/mod/1783");
    }

    [Fact]
    public async Task Install_ShowsThePlanDialogRatherThanInstallingStraightAway()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        await fixture.Browser.Results.Single(card => card.Name == "Config lib").InstallCommand.ExecuteAsync(null);

        var dialog = fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<ModInstallPlanDialogViewModel>();
        dialog.FileNameText.ShouldBe("configlib-1.11.1.zip");
        (await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfirmingThePlan_InstallsIntoTheSelectedInstanceAndReportsIt()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);
        await fixture.Browser.Results.Single(card => card.Name == "Config lib").InstallCommand.ExecuteAsync(null);
        var dialog = (ModInstallPlanDialogViewModel)fixture.Overlay.Shown[0];

        await dialog.ConfirmCommand.ExecuteAsync(null);

        (await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None)).ShouldHaveSingleItem().Identity.ShouldBe("configlib");
        fixture.Toasts.Shown.ShouldContain(toast => toast.Tone == ToastTone.Success && toast.Title == "Config lib installé");
    }

    [Fact]
    public async Task Install_WithoutATargetInstance_AsksForOneInsteadOfGuessing()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);
        fixture.Browser.SelectedInstance = fixture.Browser.TargetInstances[0];

        await fixture.Browser.Results[0].InstallCommand.ExecuteAsync(null);

        fixture.Overlay.Shown.ShouldBeEmpty();
        fixture.Toasts.Shown.ShouldContain(toast => toast.Title == "Choisis une instance");
    }

    [Fact]
    public async Task Install_ModWithoutACompatibleRelease_ReportsItWithoutInstallingAnything()
    {
        // BetterRuins n'a de release taguée que pour 1.22.0 : rien pour l'instance en 1.21.3.
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        await fixture.Browser.Results.Single(card => card.Name == "BetterRuins").InstallCommand.ExecuteAsync(null);

        fixture.Toasts.Shown.ShouldContain(toast => toast.Title == "Aucune version compatible");
        (await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_NullArguments_AreRejected()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var mods = ModDbDoubles.CreateRepository(fileSystem, instances, Paths);
        var installService = ModDbDoubles.CreateInstallService(fileSystem, instances, mods, Paths, clock);
        var client = ModDbDoubles.CreateClient(fileSystem, Paths, clock);
        var opener = new FakeExternalUrlOpener();
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();
        var logoCache = ModDbDoubles.CreateLogoCache();

        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(null!, installService, instances, opener, overlay, toasts, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, null!, instances, opener, overlay, toasts, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, installService, null!, opener, overlay, toasts, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, installService, instances, null!, overlay, toasts, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, installService, instances, opener, null!, toasts, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, installService, instances, opener, overlay, null!, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, installService, instances, opener, overlay, toasts, null!));
    }
}