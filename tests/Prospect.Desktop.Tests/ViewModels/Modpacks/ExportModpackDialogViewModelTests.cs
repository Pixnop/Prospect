using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Modpacks;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Modpacks;
using Prospect.Desktop.ViewModels.Toasts;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Modpacks;

/// <summary>
/// <see cref="ExportModpackDialogViewModel"/> : choix de forme, sélecteur de destination (via
/// <see cref="FakeFilePickerService"/>), et les deux issues possibles une fois l'export terminé
/// (fermeture directe avec toast, ou second temps listant les mods laissés de côté).
/// </summary>
public sealed class ExportModpackDialogViewModelTests
{
    private const string Slug = "homestead";
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        ExportModpackDialogViewModel ViewModel,
        FakeFilePickerService FilePicker,
        RecordingOverlayService Overlay,
        RecordingToastService Toasts,
        MockFileSystem FileSystem,
        IInstalledModRepository Mods);

    private static async Task<Fixture> CreateAsync()
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var service = new InstanceService(repository, fileSystem, new FakeClock(Now));
        await service.CreateAsync("Homestead", GameVersion.Parse("1.21.3"));
        var mods = ModDbDoubles.CreateRepository(fileSystem, repository, Paths);
        var exportService = new ModpackExportService(repository, mods, fileSystem);
        var filePicker = new FakeFilePickerService();
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();

        var viewModel = new ExportModpackDialogViewModel(Slug, "Homestead", exportService, filePicker, overlay, toasts);

        return new Fixture(viewModel, filePicker, overlay, toasts, fileSystem, mods);
    }

    [Fact]
    public async Task Constructor_DefaultsToArchiveFormatWithModConfigOption()
    {
        var fixture = await CreateAsync();

        fixture.ViewModel.IsArchive.ShouldBeTrue();
        fixture.ViewModel.IsManifestOnly.ShouldBeFalse();
        fixture.ViewModel.ShowModConfigOption.ShouldBeTrue();
        fixture.ViewModel.Title.ShouldBe("Exporter « Homestead »");
    }

    [Fact]
    public async Task SelectFormat_ManifestOnly_HidesTheModConfigOption()
    {
        var fixture = await CreateAsync();

        fixture.ViewModel.SelectFormatCommand.Execute(ModpackExportFormat.ManifestOnly);

        fixture.ViewModel.IsManifestOnly.ShouldBeTrue();
        fixture.ViewModel.IsArchive.ShouldBeFalse();
        fixture.ViewModel.ShowModConfigOption.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancel_ClosesTheOverlay()
    {
        var fixture = await CreateAsync();
        fixture.Overlay.Show(fixture.ViewModel);

        fixture.ViewModel.CancelCommand.Execute(null);

        fixture.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public async Task ExportAsync_PickerCanceled_StaysOnTheFormWithoutExporting()
    {
        var fixture = await CreateAsync();
        fixture.FilePicker.NextSavePath = null;
        fixture.Overlay.Show(fixture.ViewModel);

        await fixture.ViewModel.ExportCommand.ExecuteAsync(null);

        fixture.Overlay.Active.ShouldBe(fixture.ViewModel);
        fixture.ViewModel.IsResultPhase.ShouldBeFalse();
        fixture.Toasts.Shown.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExportAsync_SuggestsAFileNameDerivedFromTheInstanceNameWithTheChosenExtension()
    {
        var fixture = await CreateAsync();
        fixture.FilePicker.NextSavePath = null;

        await fixture.ViewModel.ExportCommand.ExecuteAsync(null);

        var request = fixture.FilePicker.SaveRequests.ShouldHaveSingleItem();
        request.SuggestedFileName.ShouldBe("homestead.zip");
        request.Extension.ShouldBe("zip");
    }

    [Fact]
    public async Task ExportAsync_NoModsToSkip_ClosesAndShowsASuccessToast()
    {
        var fixture = await CreateAsync();
        fixture.FilePicker.NextSavePath = "/out/homestead.zip";
        fixture.Overlay.Show(fixture.ViewModel);

        await fixture.ViewModel.ExportCommand.ExecuteAsync(null);

        fixture.Overlay.Active.ShouldBeNull();
        fixture.ViewModel.ModsExportedCount.ShouldBe(0);
        var toast = fixture.Toasts.Shown.ShouldHaveSingleItem();
        toast.Tone.ShouldBe(ToastTone.Success);
        fixture.FileSystem.File.Exists("/out/homestead.zip").ShouldBeTrue();
    }

    [Fact]
    public async Task ExportAsync_ManifestOnlyFormat_WritesAJsonFileDirectly()
    {
        var fixture = await CreateAsync();
        fixture.ViewModel.SelectFormatCommand.Execute(ModpackExportFormat.ManifestOnly);
        fixture.FilePicker.NextSavePath = "/out/homestead.json";

        await fixture.ViewModel.ExportCommand.ExecuteAsync(null);

        fixture.FileSystem.File.Exists("/out/homestead.json").ShouldBeTrue();
    }

    [Fact]
    public async Task ExportAsync_UnidentifiedModPresent_StaysOpenAndListsIt()
    {
        var fixture = await CreateAsync();
        var modsDirectory = fixture.Mods.GetModsDirectory(Slug);
        fixture.FileSystem.AddFile(
            fixture.FileSystem.Path.Combine(modsDirectory, "mystery.zip"),
            new MockFileData(ModDbDoubles.BuildArchive(null)));
        fixture.FilePicker.NextSavePath = "/out/homestead.zip";
        fixture.Overlay.Show(fixture.ViewModel);

        await fixture.ViewModel.ExportCommand.ExecuteAsync(null);

        fixture.Overlay.Active.ShouldBe(fixture.ViewModel);
        fixture.ViewModel.IsResultPhase.ShouldBeTrue();
        var skipped = fixture.ViewModel.SkippedMods.ShouldHaveSingleItem();
        skipped.FileName.ShouldBe("mystery.zip");
        // Le toast de succès part quand même : l'export lui-même a réussi, seul ce mod n'a pas pu voyager.
        fixture.Toasts.Shown.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CloseResult_ClosesTheOverlay()
    {
        var fixture = await CreateAsync();
        fixture.Overlay.Show(fixture.ViewModel);

        fixture.ViewModel.CloseResultCommand.Execute(null);

        fixture.Overlay.Active.ShouldBeNull();
    }
}