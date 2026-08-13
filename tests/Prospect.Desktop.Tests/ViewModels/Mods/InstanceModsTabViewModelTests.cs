using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Mods;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Mods;

public sealed class InstanceModsTabViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        InstanceModsTabViewModel Tab,
        IInstalledModRepository Mods,
        FakeModDbHandler Server,
        IModUpdateCheckCache UpdateCache,
        FakeClock Clock,
        RecordingOverlayService Overlay,
        RecordingToastService Toasts,
        MockFileSystem FileSystem,
        string Slug);

    private static async Task<Fixture> CreateAsync()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var service = new InstanceService(instances, fileSystem, clock);
        var record = await service.CreateAsync("Homestead", GameVersion.Parse("1.21.3"));

        var mods = ModDbDoubles.CreateRepository(fileSystem, instances, Paths);
        var handler = new FakeModDbHandler();
        var installService = ModDbDoubles.CreateInstallService(fileSystem, instances, mods, Paths, clock, handler);
        var updateChecker = ModDbDoubles.CreateUpdateChecker(fileSystem, instances, mods, Paths, clock, handler);
        var updateCache = new ModUpdateCheckCache();
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();

        return new Fixture(
            new InstanceModsTabViewModel(record.Slug, mods, installService, updateChecker, updateCache, clock, overlay, toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()),
            mods,
            handler,
            updateCache,
            clock,
            overlay,
            toasts,
            fileSystem,
            record.Slug);
    }

    private static void SeedMod(Fixture fixture, string fileName, string modId, string name, string? dependency = null)
        => ModDbDoubles.SeedMod(
            fixture.FileSystem,
            fixture.Mods.GetModsDirectory(fixture.Slug),
            fileName,
            ModDbDoubles.ModInfo(modId, name, "1.0.0", dependency));

    [Fact]
    public async Task RefreshAsync_NoMods_ShowsTheEmptyState()
    {
        var fixture = await CreateAsync();

        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        fixture.Tab.HasMods.ShouldBeFalse();
        fixture.Tab.Mods.ShouldBeEmpty();
        fixture.Tab.SummaryText.ShouldBeEmpty();
    }

    [Fact]
    public async Task RefreshAsync_ListsEnabledAndDisabledModsWithTheirState()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        SeedMod(fixture, "extrainfo-1.0.0.zip.disabled", "extrainfo", "Extra Info");

        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        fixture.Tab.HasMods.ShouldBeTrue();
        fixture.Tab.Mods.Count.ShouldBe(2);
        fixture.Tab.Mods.Single(row => row.Name == "Extra Info").IsEnabled.ShouldBeFalse();
        fixture.Tab.SummaryText.ShouldBe("2 mods installés · 1 désactivés");
    }

    [Fact]
    public async Task RefreshAsync_ManuallyDroppedArchive_IsBadgedAsManual()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");

        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        var row = fixture.Tab.Mods.ShouldHaveSingleItem();
        row.IsFromModDb.ShouldBeFalse();
        row.ProvenanceText.ShouldBe("manuel");
        row.SideText.ShouldBe("universel");
    }

    [Fact]
    public async Task RefreshAsync_UnreadableArchive_IsBadgedUnidentifiedWithItsReason()
    {
        var fixture = await CreateAsync();
        fixture.FileSystem.AddFile(
            fixture.FileSystem.Path.Combine(fixture.Mods.GetModsDirectory(fixture.Slug), "broken.zip"),
            new MockFileData("pas une archive"));

        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        var row = fixture.Tab.Mods.ShouldHaveSingleItem();
        row.IsUnidentified.ShouldBeTrue();
        row.UnidentifiedReason.ShouldBe("archive illisible");
        row.VersionText.ShouldBe("version inconnue");
    }

    [Fact]
    public async Task TogglingARow_RenamesTheArchiveAndReportsIt()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        fixture.Tab.Mods[0].IsEnabled = false;
        await Task.Yield();

        (await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None)).ShouldHaveSingleItem().IsEnabled.ShouldBeFalse();
        fixture.Toasts.Shown.ShouldContain(toast => toast.Title == "Mod désactivé");
    }

    [Fact]
    public async Task RemovingAMod_OpensAConfirmationThatNamesWhatDependsOnIt()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "vsimgui-1.0.0.zip", "vsimgui", "VS ImGui");
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib", dependency: "vsimgui");
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        var target = fixture.Tab.Mods.Single(row => row.Name == "VS ImGui");
        await target.RemoveCommand.ExecuteAsync(null);

        var dialog = fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<UninstallModDialogViewModel>();
        dialog.HasDependents.ShouldBeTrue();
        dialog.DependentsMessage.ShouldBe("Le mod « Config lib » en dépend et risque de ne plus fonctionner.");
    }

    [Fact]
    public async Task RemovingAModNobodyNeeds_ShowsNoDependencyWarning()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        await fixture.Tab.Mods[0].RemoveCommand.ExecuteAsync(null);

        fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<UninstallModDialogViewModel>().HasDependents.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmingRemoval_DeletesTheArchiveAndRefreshesTheList()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);
        await fixture.Tab.Mods[0].RemoveCommand.ExecuteAsync(null);
        var dialog = (UninstallModDialogViewModel)fixture.Overlay.Shown[0];

        await dialog.ConfirmCommand.ExecuteAsync(null);

        fixture.Tab.Mods.ShouldBeEmpty();
        fixture.Toasts.Shown.ShouldContain(toast => toast.Title == "Mod retiré");
        fixture.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public async Task CancellingRemoval_KeepsTheMod()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);
        await fixture.Tab.Mods[0].RemoveCommand.ExecuteAsync(null);

        ((UninstallModDialogViewModel)fixture.Overlay.Shown[0]).CancelCommand.Execute(null);

        (await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task BrowseCommand_RaisesTheRequestWithTheInstanceSlug()
    {
        var fixture = await CreateAsync();
        string? requested = null;
        fixture.Tab.BrowseRequested += (_, slug) => requested = slug;

        fixture.Tab.BrowseCommand.Execute(null);

        requested.ShouldBe(fixture.Slug);
    }

    [Fact]
    public async Task ModsDirectoryText_PointsAtTheGameDataPath()
    {
        var fixture = await CreateAsync();

        fixture.Tab.ModsDirectoryText.ShouldEndWith(fixture.FileSystem.Path.Combine("data", "Mods"));
    }

    // ── Vérification des mises à jour (feature 4b) ────────────────────────────────

    // Reprend exactement la release que ConfigLibJson du FakeModDbHandler déclare déjà (mêmes
    // identifiants, même version, même tag de version de jeu) : /api/updates et /api/mod/configlib
    // restent cohérents entre eux, comme le seraient deux endpoints du vrai ModDB.
    private const string ConfigLibUpdateJson = """
    {
      "statuscode": "200",
      "updates": {
        "configlib": {
          "releaseid": 38314, "fileid": 84120, "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.11.1.zip",
          "filename": "configlib_1.11.1.zip", "downloads": 90210, "tags": ["1.21.3"], "modidstr": "configlib",
          "modversion": "1.11.1", "changelog": null, "created": "2026-02-11 09:22:10"
        }
      }
    }
    """;

    [Fact]
    public async Task LastCheckedText_BeforeAnyCheck_SaysNever()
    {
        var fixture = await CreateAsync();

        fixture.Tab.LastCheckedText.ShouldBe("Dernière vérification : jamais");
    }

    [Fact]
    public async Task CheckUpdatesAsync_UpdateFound_BadgesTheRowAndSummarizesTheCount()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        fixture.Server.UpdatesJson = ConfigLibUpdateJson;
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);

        var row = fixture.Tab.Mods.ShouldHaveSingleItem();
        row.HasUpdateAvailable.ShouldBeTrue();
        row.UpdateResult!.AvailableRelease!.Version.ShouldBe(ModVersion.Parse("1.11.1"));
        fixture.Tab.AvailableUpdateCount.ShouldBe(1);
        fixture.Tab.HasAvailableUpdates.ShouldBeTrue();
        fixture.Tab.UpdatesAvailableTitle.ShouldBe("1 mise à jour disponible");
        fixture.Tab.LastCheckedText.ShouldBe("Dernière vérification : aujourd'hui");
    }

    [Fact]
    public async Task CheckUpdatesAsync_NoUpdateFound_RowShowsNoBadge()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);

        fixture.Tab.Mods.ShouldHaveSingleItem().HasUpdateAvailable.ShouldBeFalse();
        fixture.Tab.HasAvailableUpdates.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckUpdatesAsync_DisabledMod_IsStillCheckedAndCanBeBadged()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip.disabled", "configlib", "Config lib");
        fixture.Server.UpdatesJson = ConfigLibUpdateJson;
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);

        var row = fixture.Tab.Mods.ShouldHaveSingleItem();
        row.IsEnabled.ShouldBeFalse();
        row.HasUpdateAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckUpdatesAsync_StoresTheReportInTheSharedCacheForTheHomeCardPill()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        fixture.Server.UpdatesJson = ConfigLibUpdateJson;
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);

        fixture.UpdateCache.TryGet(fixture.Slug)!.UpdateCount.ShouldBe(1);
    }

    [Fact]
    public async Task CheckUpdatesAsync_ModDbUnreachable_ShowsAnErrorToastRatherThanCrashing()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        fixture.Server.IsOnline = false;
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);

        fixture.Toasts.Shown.ShouldContain(toast => toast.Title == "Vérification impossible");
        fixture.Tab.Mods.ShouldHaveSingleItem().HasUpdateAvailable.ShouldBeFalse();
    }

    // ── Mise à jour d'un mod ─────────────────────────────────────────────────────

    [Fact]
    public async Task RequestingAnUpdate_OpensThePlanDialogWithTheTargetVersion()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        fixture.Server.UpdatesJson = ConfigLibUpdateJson;
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);
        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);

        await fixture.Tab.Mods.ShouldHaveSingleItem().UpdateCommand.ExecuteAsync(null);

        var dialog = fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<ModUpdatePlanDialogViewModel>();
        dialog.Plan.Updated.Version.ShouldBe(ModVersion.Parse("1.11.1"));
    }

    [Fact]
    public async Task RequestingAnUpdate_OtherInstalledModsDependingOnIt_AreListedInformativelyInTheDialog()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        SeedMod(fixture, "carrycapacity-1.0.0.zip", "carrycapacity", "Carry Capacity", dependency: "configlib");
        fixture.Server.UpdatesJson = ConfigLibUpdateJson;
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);
        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);

        var configLibRow = fixture.Tab.Mods.Single(row => row.Name == "Config lib");
        await configLibRow.UpdateCommand.ExecuteAsync(null);

        var dialog = (ModUpdatePlanDialogViewModel)fixture.Overlay.Shown[0];
        dialog.HasDependents.ShouldBeTrue();
        dialog.DependentsNote.ShouldBe("« Carry Capacity » dépend de ce mod.");
    }

    [Fact]
    public async Task ConfirmingAnUpdate_ReplacesTheFileAndInvalidatesTheKnownUpdateState()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        fixture.Server.UpdatesJson = ConfigLibUpdateJson;
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);
        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);
        await fixture.Tab.Mods.ShouldHaveSingleItem().UpdateCommand.ExecuteAsync(null);
        var dialog = (ModUpdatePlanDialogViewModel)fixture.Overlay.Shown[0];

        await dialog.ConfirmCommand.ExecuteAsync(null);

        var row = fixture.Tab.Mods.ShouldHaveSingleItem();
        row.Mod.Version.ShouldBe(ModVersion.Parse("1.11.1"));
        row.HasUpdateAvailable.ShouldBeFalse();
        fixture.Overlay.Active.ShouldBeNull();
        fixture.Toasts.Shown.ShouldContain(toast => toast.Title == "Config lib mis à jour");

        // Plus aucune affirmation de fraîcheur tant qu'une nouvelle vérification n'a pas eu lieu :
        // même règle que la pastille de la carte d'Accueil (voir IModUpdateCheckCache).
        fixture.Tab.LastCheckedText.ShouldBe("Dernière vérification : jamais");
        fixture.UpdateCache.TryGet(fixture.Slug).ShouldBeNull();
    }

    [Fact]
    public async Task TogglingAMod_InvalidatesTheKnownUpdateState()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        fixture.Server.UpdatesJson = ConfigLibUpdateJson;
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);
        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);
        fixture.Tab.AvailableUpdateCount.ShouldBe(1);

        fixture.Tab.Mods[0].IsEnabled = false;
        await Task.Yield();

        fixture.Tab.AvailableUpdateCount.ShouldBe(0);
        fixture.UpdateCache.TryGet(fixture.Slug).ShouldBeNull();
    }

    // ── Tout mettre à jour ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAllCommand_CannotExecute_WithoutAnyKnownUpdate()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);
        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);

        fixture.Tab.UpdateAllCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateAllCommand_ApplyAllAndReportsProgressThenClearsIt()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        fixture.Server.UpdatesJson = ConfigLibUpdateJson;
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);
        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);
        fixture.Tab.UpdateAllCommand.CanExecute(null).ShouldBeTrue();

        await fixture.Tab.UpdateAllCommand.ExecuteAsync(null);

        fixture.Tab.Mods.ShouldHaveSingleItem().Mod.Version.ShouldBe(ModVersion.Parse("1.11.1"));
        fixture.Tab.UpdateAllProgressText.ShouldBeEmpty();
        fixture.Tab.AvailableUpdateCount.ShouldBe(0);
        fixture.Toasts.Shown.ShouldContain(toast => toast.Title == "1 mod mis à jour");
    }

    [Fact]
    public async Task Constructor_NullArguments_AreRejected()
    {
        var fixture = await CreateAsync();
        var fileSystem = new MockFileSystem();
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var mods = ModDbDoubles.CreateRepository(fileSystem, instances, Paths);
        var installService = ModDbDoubles.CreateInstallService(fileSystem, instances, mods, Paths, new FakeClock(Now));
        var updateChecker = ModDbDoubles.CreateUpdateChecker(fileSystem, instances, mods, Paths, new FakeClock(Now));
        var updateCache = new ModUpdateCheckCache();
        var clock = new FakeClock(Now);

        Should.Throw<ArgumentException>(() => new InstanceModsTabViewModel(string.Empty, mods, installService, updateChecker, updateCache, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel("slug", null!, installService, updateChecker, updateCache, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel("slug", mods, null!, updateChecker, updateCache, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel("slug", mods, installService, null!, updateCache, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel("slug", mods, installService, updateChecker, null!, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel("slug", mods, installService, updateChecker, updateCache, null!, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel("slug", mods, installService, updateChecker, updateCache, clock, null!, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel("slug", mods, installService, updateChecker, updateCache, clock, fixture.Overlay, null!, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
    }
}