using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
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
        var installService = ModDbDoubles.CreateInstallService(fileSystem, instances, mods, Paths, clock);
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();

        return new Fixture(
            new InstanceModsTabViewModel(record.Slug, mods, installService, overlay, toasts),
            mods,
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

    [Fact]
    public async Task Constructor_NullArguments_AreRejected()
    {
        var fixture = await CreateAsync();
        var fileSystem = new MockFileSystem();
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var mods = ModDbDoubles.CreateRepository(fileSystem, instances, Paths);
        var installService = ModDbDoubles.CreateInstallService(fileSystem, instances, mods, Paths, new FakeClock(Now));

        Should.Throw<ArgumentException>(() => new InstanceModsTabViewModel(string.Empty, mods, installService, fixture.Overlay, fixture.Toasts));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel("slug", null!, installService, fixture.Overlay, fixture.Toasts));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel("slug", mods, null!, fixture.Overlay, fixture.Toasts));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel("slug", mods, installService, null!, fixture.Toasts));
        Should.Throw<ArgumentNullException>(() => new InstanceModsTabViewModel("slug", mods, installService, fixture.Overlay, null!));
    }
}