using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Diagnostics;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Settings;
using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
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
        string Slug,
        string LogPath);

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
        var logInsights = new GameLogInsightsService(fileSystem, mods, new ModIntegrationScanner(fileSystem), clock);
        var logInsightsCache = new GameLogInsightsCache();
        var logPath = fileSystem.Path.Combine(Paths.LogsDirectory, $"instance-{record.Slug}.log");
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();

        return new Fixture(
            new InstanceModsTabViewModel(
                record.Slug, mods, installService, updateChecker, updateCache, logInsights, logInsightsCache, logPath,
                clock, overlay, toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()),
            mods,
            handler,
            updateCache,
            clock,
            overlay,
            toasts,
            fileSystem,
            record.Slug,
            logPath);
    }

    private static void SeedMod(Fixture fixture, string fileName, string modId, string name, string? dependency = null, string version = "1.0.0")
        => ModDbDoubles.SeedMod(
            fixture.FileSystem,
            fixture.Mods.GetModsDirectory(fixture.Slug),
            fileName,
            ModDbDoubles.ModInfo(modId, name, version, dependency));

    // Un journal de lancement aux formes du vrai jeu (voir GameLogAnalyzerTests) : un mod à qui
    // deux erreurs sont attribuables, un autre qui n'a rien à se reprocher.
    private static void SeedLaunchLog(Fixture fixture, string content)
        => fixture.FileSystem.AddFile(fixture.LogPath, new MockFileData(content.ReplaceLineEndings("\n")));

    private const string LogWithConfigLibErrors = """
    13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: configlib, extrainfo, game
    13.8.2026 21:08:23 [Client Error] [configlib] Could not resolve some dependencies:
    13.8.2026 21:08:23 [Client Error] [configlib]     saltyseas - Missing
    13.8.2026 21:08:24 [Client Warning] [configlib] a shape is missing, using a cube instead
    """;

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

    /// <summary>
    /// Une vérification qui ne trouve rien doit quand même RENDRE UN VERDICT. Sans lui, elle laisse
    /// l'écran exactement dans l'état où elle l'a trouvé, et un bouton qui ne change rien passe pour
    /// un bouton cassé : c'est le « ça ne fonctionne pas » remonté du terrain.
    /// </summary>
    [Fact]
    public async Task CheckUpdatesAsync_NothingToReport_StillSaysSoOutLoud()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.11.1.zip", "configlib", "Config lib", version: "1.11.1");
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        fixture.Tab.HasCheckVerdict.ShouldBeFalse("rien n'a encore été vérifié");

        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);

        fixture.Tab.HasCheckVerdict.ShouldBeTrue();
        fixture.Tab.CheckVerdictText.ShouldContain("1 mod vérifié");
    }

    /// <summary>
    /// Le cas de terrain : le ModDB signale une release plus récente, dont les tags s'arrêtent avant
    /// la version de l'instance. Elle ne repasse plus pour « à jour ».
    /// </summary>
    [Fact]
    public async Task CheckUpdatesAsync_ANewerReleaseNotDeclaredForThisVersion_IsBadgedAndCounted()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");

        // Instance en 1.21.3, release plus récente taguée pour une autre série.
        fixture.Server.UpdatesJson = ConfigLibUpdateJson.Replace(@"""tags"": [""1.21.3""]", @"""tags"": [""1.20.4""]", StringComparison.Ordinal);
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        await fixture.Tab.CheckUpdatesCommand.ExecuteAsync(null);

        var row = fixture.Tab.Mods.ShouldHaveSingleItem();
        row.HasUpdateAvailable.ShouldBeFalse("pas de mise à jour d'un clic");
        row.HasUndeclaredUpdate.ShouldBeTrue();
        row.UndeclaredUpdateText.ShouldContain("1.11.1");
        row.UndeclaredUpdateText.ShouldContain("1.20.4");

        fixture.Tab.UndeclaredUpdateCount.ShouldBe(1);
        fixture.Tab.CheckVerdictText.ShouldContain("non déclarée");
    }

    /// <summary>Le verdict existe aussi en anglais, avec la même grammaire de comptage.</summary>
    [Fact]
    public void CheckVerdict_ReadsInEnglishToo()
    {
        var english = UiText.TableFor(ProspectSettings.English).Mods;

        english.CheckVerdict(0, 0, 3).ShouldBe("3 mods checked: everything is up to date.");
        english.CheckVerdict(0, 1, 3).ShouldBe("3 mods checked: 1 newer version exists, not declared for your game version.");
        english.CheckVerdict(2, 1, 4).ShouldBe("4 mods checked: 2 updates available, and 1 newer version not declared.");
        english.CheckVerdict(0, 0, 0).ShouldBe("No mod to check.");
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

    // ── Ce que le journal du dernier lancement dit des mods ──────────────────────────────────

    [Fact]
    public async Task RefreshAsync_NoLaunchYet_LeavesEveryRowSilent()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");

        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        var row = fixture.Tab.Mods.ShouldHaveSingleItem();
        row.HasLogErrors.ShouldBeFalse();
        row.HasLogWarnings.ShouldBeFalse();
        row.HasIntegration.ShouldBeFalse();
    }

    [Fact]
    public async Task RefreshAsync_LastLaunchBlamedAMod_BadgesThatRowAndQuotesTheLines()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        SeedMod(fixture, "extrainfo-1.0.0.zip", "extrainfo", "Extra Info");
        SeedLaunchLog(fixture, LogWithConfigLibErrors);

        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        var blamed = fixture.Tab.Mods.Single(row => row.Name == "Config lib");
        blamed.HasLogErrors.ShouldBeTrue();
        blamed.LogErrorsText.ShouldBe("2 erreurs au dernier lancement");
        blamed.HasLogWarnings.ShouldBeTrue();
        blamed.LogWarningsText.ShouldBe("1 avertissement");
        blamed.LogProblemTooltip.ShouldContain("saltyseas");

        fixture.Tab.Mods.Single(row => row.Name == "Extra Info").HasLogErrors.ShouldBeFalse();
    }

    /// <summary>
    /// Le journal EST la persistance, et le relire à chaque va-et-vient entre onglets serait le
    /// relire pour rien : la deuxième lecture doit venir du cache de session, ce que prouve un
    /// journal effacé entre les deux.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_Twice_KeepsWhatTheFirstReadingFound()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        SeedLaunchLog(fixture, LogWithConfigLibErrors);
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        fixture.FileSystem.File.Delete(fixture.LogPath);
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        fixture.Tab.Mods.ShouldHaveSingleItem().HasLogErrors.ShouldBeTrue();
    }

    [Fact]
    public async Task ReloadAfterExitAsync_ReadsTheJournalTheGameJustFinishedWriting()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);
        fixture.Tab.Mods.ShouldHaveSingleItem().HasLogErrors.ShouldBeFalse();

        SeedLaunchLog(fixture, LogWithConfigLibErrors);
        await fixture.Tab.ReloadAfterExitAsync();

        fixture.Tab.Mods.ShouldHaveSingleItem().HasLogErrors.ShouldBeTrue();
    }

    /// <summary>
    /// Un lancement tronque le journal : garder les pastilles pendant que le jeu tourne les ferait
    /// décrire une session qui n'existe plus.
    /// </summary>
    [Fact]
    public async Task ResetLogInsightsAsync_ForgetsWhatThePreviousLaunchSaid()
    {
        var fixture = await CreateAsync();
        SeedMod(fixture, "configlib-1.0.0.zip", "configlib", "Config lib");
        SeedLaunchLog(fixture, LogWithConfigLibErrors);
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        fixture.FileSystem.File.Delete(fixture.LogPath);
        await fixture.Tab.ResetLogInsightsAsync();

        fixture.Tab.Mods.ShouldHaveSingleItem().HasLogErrors.ShouldBeFalse();
    }

    /// <summary>
    /// Les intégrations dépendent de ce qui est INSTALLÉ, pas seulement de ce que le dernier
    /// lancement a dit : retirer la cible doit changer le verdict tout de suite, sans attendre un
    /// lancement de plus.
    /// </summary>
    [Fact]
    public async Task UninstallingTheTargetMod_TurnsWorksWithIntoAWaitingReference()
    {
        var fixture = await CreateAsync();
        ModDbDoubles.SeedMod(
            fixture.FileSystem,
            fixture.Mods.GetModsDirectory(fixture.Slug),
            "carryon-1.0.0.zip",
            ModDbDoubles.ModInfo("carryon", "Carry On", "1.0.0"),
            extraEntries: new Dictionary<string, string>
            {
                ["assets/carryon/patches/crates.json"] = """[{ "file": "bettercrates:blocktypes/a", "op": "add", "path": "/x", "value": 1 }]""",
            });
        SeedMod(fixture, "bettercrates-1.0.0.zip", "bettercrates", "Better Crates");
        await fixture.Tab.RefreshCommand.ExecuteAsync(null);
        fixture.Tab.Mods.Single(row => row.Name == "Carry On").IntegrationText.ShouldBe("fonctionne avec Better Crates");

        await fixture.Tab.Mods.Single(row => row.Name == "Better Crates").RemoveCommand.ExecuteAsync(null);
        await ((UninstallModDialogViewModel)fixture.Overlay.Active!).ConfirmCommand.ExecuteAsync(null);

        fixture.Tab.Mods.ShouldHaveSingleItem().IntegrationText.ShouldBe("attend du contenu de bettercrates");
    }

    [Fact]
    public async Task RefreshAsync_ModThatPatchesAnInstalledMod_SaysItWorksWithIt()
    {
        var fixture = await CreateAsync();
        ModDbDoubles.SeedMod(
            fixture.FileSystem,
            fixture.Mods.GetModsDirectory(fixture.Slug),
            "carryon-1.0.0.zip",
            ModDbDoubles.ModInfo("carryon", "Carry On", "1.0.0"),
            extraEntries: new Dictionary<string, string>
            {
                ["assets/carryon/patches/crates.json"] = """[{ "file": "bettercrates:blocktypes/a", "op": "add", "path": "/x", "value": 1 }]""",
            });
        SeedMod(fixture, "bettercrates-1.0.0.zip", "bettercrates", "Better Crates");

        await fixture.Tab.RefreshCommand.ExecuteAsync(null);

        var carryon = fixture.Tab.Mods.Single(row => row.Name == "Carry On");
        carryon.HasIntegration.ShouldBeTrue();
        carryon.IntegrationText.ShouldBe("fonctionne avec Better Crates");
        fixture.Tab.Mods.Single(row => row.Name == "Better Crates").HasIntegration.ShouldBeFalse();
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
        var logInsights = new GameLogInsightsService(fileSystem, mods, new ModIntegrationScanner(fileSystem), clock);
        var logInsightsCache = new GameLogInsightsCache();
        var logPath = fileSystem.Path.Combine(Paths.LogsDirectory, "instance-slug.log");

        Should.Throw<ArgumentException>(() => new InstanceModsTabViewModel(
            string.Empty, mods, installService, updateChecker, updateCache, logInsights, logInsightsCache, logPath, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel(
            "slug", null!, installService, updateChecker, updateCache, logInsights, logInsightsCache, logPath, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel(
            "slug", mods, null!, updateChecker, updateCache, logInsights, logInsightsCache, logPath, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel(
            "slug", mods, installService, null!, updateCache, logInsights, logInsightsCache, logPath, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel(
            "slug", mods, installService, updateChecker, null!, logInsights, logInsightsCache, logPath, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel(
            "slug", mods, installService, updateChecker, updateCache, null!, logInsightsCache, logPath, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel(
            "slug", mods, installService, updateChecker, updateCache, logInsights, null!, logPath, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentException>(() => new InstanceModsTabViewModel(
            "slug", mods, installService, updateChecker, updateCache, logInsights, logInsightsCache, string.Empty, clock, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel(
            "slug", mods, installService, updateChecker, updateCache, logInsights, logInsightsCache, logPath, null!, fixture.Overlay, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel(
            "slug", mods, installService, updateChecker, updateCache, logInsights, logInsightsCache, logPath, clock, null!, fixture.Toasts, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel(
            "slug", mods, installService, updateChecker, updateCache, logInsights, logInsightsCache, logPath, clock, fixture.Overlay, null!, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
    }
}