using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Http;
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
/// <see cref="ImportModpackViewModel"/> : chargement de l'aperçu, confirmation, rapport groupé par
/// catégorie. Le ModDB factice (<see cref="FakeModDbHandler"/>, partagé avec le domaine ModDb) sert
/// de <c>configlib</c> connu pour les cas de succès.
/// </summary>
public sealed class ImportModpackViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly GameVersion SampleVersion = GameVersion.Parse("1.21.3");

    private sealed record Fixture(
        ModpackImportService ImportService,
        RecordingOverlayService Overlay,
        RecordingToastService Toasts,
        MockFileSystem FileSystem,
        IInstalledGameVersionRepository GameVersions,
        FakeModDbHandler Handler);

    private static Fixture CreateServices(bool gameVersionInstalled = true)
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var instanceService = new InstanceService(repository, fileSystem, clock);
        var handler = new FakeModDbHandler();
        var modDbClient = ModDbDoubles.CreateClient(fileSystem, Paths, clock, handler);
        var downloads = new DownloadManager(new HttpClient(handler, disposeHandler: false), fileSystem, Paths, clock);
        var mods = ModDbDoubles.CreateRepository(fileSystem, repository, Paths);
        var gameVersions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var catalog = new FakeGameVersionCatalog { Catalog = FakeGameVersionCatalog.Build("1.21.3") };
        var gameInstall = new GameInstallService(catalog, downloads, gameVersions, new FakeGameInstallStrategy());

        if (gameVersionInstalled)
        {
            gameVersions.PrepareDirectory(SampleVersion);
            gameVersions.MarkCompleteAsync(SampleVersion, CancellationToken.None).GetAwaiter().GetResult();
        }

        var importService = new ModpackImportService(modDbClient, downloads, mods, instanceService, repository, gameInstall, gameVersions, catalog, fileSystem, clock);

        return new Fixture(importService, new RecordingOverlayService(), new RecordingToastService(), fileSystem, gameVersions, handler);
    }

    private static async Task<string> WriteManifestAsync(MockFileSystem fileSystem, ModpackManifest manifest, string path = "/import/pack.json")
    {
        fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(path)!);
        var stream = fileSystem.File.Create(path);
        await using (stream.ConfigureAwait(false))
        {
            await ModpackManifestSerializer.WriteAsync(stream, manifest, CancellationToken.None);
        }

        return path;
    }

    private static ImportModpackViewModel CreateViewModel(Fixture fixture, string sourcePath)
        => new(sourcePath, fixture.ImportService, fixture.Overlay, fixture.Toasts, new ImmediateUiDispatcher());

    // ── Aperçu ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadPreviewAsync_ValidManifest_PopulatesTheHeaderAndInstanceNameField()
    {
        var fixture = CreateServices();
        var manifest = new ModpackManifest { SchemaVersion = 1, Name = "Pack de survie", GameVersion = SampleVersion, Mods = [] };
        var path = await WriteManifestAsync(fixture.FileSystem, manifest);
        var viewModel = CreateViewModel(fixture, path);

        await viewModel.LoadPreviewCommand.ExecuteAsync(null);

        viewModel.Step.ShouldBe(ImportModpackStep.Preview);
        viewModel.PackName.ShouldBe("Pack de survie");
        viewModel.InstanceName.ShouldBe("Pack de survie");
        viewModel.PreviewSubtitle.ShouldBe("1.21.3");
    }

    [Fact]
    public async Task LoadPreviewAsync_GameVersionAlreadyInstalled_HidesTheWarning()
    {
        var fixture = CreateServices(gameVersionInstalled: true);
        var manifest = new ModpackManifest { SchemaVersion = 1, Name = "Pack", GameVersion = SampleVersion, Mods = [] };
        var path = await WriteManifestAsync(fixture.FileSystem, manifest);
        var viewModel = CreateViewModel(fixture, path);

        await viewModel.LoadPreviewCommand.ExecuteAsync(null);

        viewModel.ShowGameVersionWarning.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadPreviewAsync_GameVersionMissing_ShowsTheWarningWithTheDownloadSize()
    {
        var fixture = CreateServices(gameVersionInstalled: false);
        var manifest = new ModpackManifest { SchemaVersion = 1, Name = "Pack", GameVersion = SampleVersion, Mods = [] };
        var path = await WriteManifestAsync(fixture.FileSystem, manifest);
        var viewModel = CreateViewModel(fixture, path);

        await viewModel.LoadPreviewCommand.ExecuteAsync(null);

        viewModel.ShowGameVersionWarning.ShouldBeTrue();
        viewModel.GameVersionWarningText.ShouldContain("1.21.3");
    }

    [Fact]
    public async Task LoadPreviewAsync_InvalidSource_MovesToFailedStepWithAMessage()
    {
        var fixture = CreateServices();
        fixture.FileSystem.AddFile("/import/garbage.json", new MockFileData("{ not json"));
        var viewModel = CreateViewModel(fixture, "/import/garbage.json");

        await viewModel.LoadPreviewCommand.ExecuteAsync(null);

        viewModel.Step.ShouldBe(ImportModpackStep.Failed);
        viewModel.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CancelPreview_ClosesTheOverlay()
    {
        var fixture = CreateServices();
        var manifest = new ModpackManifest { SchemaVersion = 1, Name = "Pack", GameVersion = SampleVersion, Mods = [] };
        var path = await WriteManifestAsync(fixture.FileSystem, manifest);
        var viewModel = CreateViewModel(fixture, path);
        fixture.Overlay.Show(viewModel);

        viewModel.CancelPreviewCommand.Execute(null);

        fixture.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public async Task InstanceNameError_BlankName_PreventsConfirm()
    {
        var fixture = CreateServices();
        var manifest = new ModpackManifest { SchemaVersion = 1, Name = "Pack", GameVersion = SampleVersion, Mods = [] };
        var path = await WriteManifestAsync(fixture.FileSystem, manifest);
        var viewModel = CreateViewModel(fixture, path);
        await viewModel.LoadPreviewCommand.ExecuteAsync(null);

        viewModel.InstanceName = "   ";

        viewModel.InstanceNameError.ShouldNotBeNullOrWhiteSpace();
        viewModel.ConfirmCommand.CanExecute(null).ShouldBeFalse();
    }

    // ── Import et rapport ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmAsync_KnownMod_ReportsItInstalledAndRaisesImported()
    {
        var fixture = CreateServices();
        var manifest = new ModpackManifest
        {
            SchemaVersion = 1,
            Name = "Pack avec configlib",
            GameVersion = SampleVersion,
            Mods = [new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1") }],
        };
        var path = await WriteManifestAsync(fixture.FileSystem, manifest);
        var viewModel = CreateViewModel(fixture, path);
        await viewModel.LoadPreviewCommand.ExecuteAsync(null);
        InstanceRecord? imported = null;
        viewModel.Imported += (_, record) => imported = record;

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        viewModel.Step.ShouldBe(ImportModpackStep.Report);
        var group = viewModel.ReportGroups.ShouldHaveSingleItem();
        group.Title.ShouldBe("1 mod installé");
        group.Rows.ShouldHaveSingleItem().ModId.ShouldBe("configlib");
        imported.ShouldNotBeNull();
        fixture.Toasts.Shown.ShouldHaveSingleItem().Tone.ShouldBe(ToastTone.Success);
    }

    [Fact]
    public async Task ConfirmAsync_UnknownMod_GroupsItUnderNotFound()
    {
        var fixture = CreateServices();
        var manifest = new ModpackManifest
        {
            SchemaVersion = 1,
            Name = "Pack cassé",
            GameVersion = SampleVersion,
            Mods = [new ModpackManifestMod { ModId = "does-not-exist", Version = ModVersion.Parse("1.0.0") }],
        };
        var path = await WriteManifestAsync(fixture.FileSystem, manifest);
        var viewModel = CreateViewModel(fixture, path);
        await viewModel.LoadPreviewCommand.ExecuteAsync(null);

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        var group = viewModel.ReportGroups.ShouldHaveSingleItem();
        group.Title.ShouldBe("1 mod introuvable sur le ModDB");
    }

    [Fact]
    public async Task ConfirmAsync_MixedOutcome_OrdersInstalledGroupBeforeFailureGroups()
    {
        var fixture = CreateServices();
        var manifest = new ModpackManifest
        {
            SchemaVersion = 1,
            Name = "Pack mixte",
            GameVersion = SampleVersion,
            Mods =
            [
                new ModpackManifestMod { ModId = "does-not-exist", Version = ModVersion.Parse("1.0.0") },
                new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1") },
            ],
        };
        var path = await WriteManifestAsync(fixture.FileSystem, manifest);
        var viewModel = CreateViewModel(fixture, path);
        await viewModel.LoadPreviewCommand.ExecuteAsync(null);

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        viewModel.ReportGroups.Select(group => group.Title).ShouldBe(["1 mod installé", "1 mod introuvable sur le ModDB"]);
    }

    [Fact]
    public async Task ConfirmAsync_NotImporting_CancelImportCannotExecute()
    {
        var fixture = CreateServices();
        var manifest = new ModpackManifest { SchemaVersion = 1, Name = "Pack", GameVersion = SampleVersion, Mods = [] };
        var path = await WriteManifestAsync(fixture.FileSystem, manifest);
        var viewModel = CreateViewModel(fixture, path);
        await viewModel.LoadPreviewCommand.ExecuteAsync(null);

        viewModel.CancelImportCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task CloseReport_ClosesTheOverlay()
    {
        var fixture = CreateServices();
        var manifest = new ModpackManifest { SchemaVersion = 1, Name = "Pack", GameVersion = SampleVersion, Mods = [] };
        var path = await WriteManifestAsync(fixture.FileSystem, manifest);
        var viewModel = CreateViewModel(fixture, path);
        await viewModel.LoadPreviewCommand.ExecuteAsync(null);
        fixture.Overlay.Show(viewModel);
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        viewModel.CloseReportCommand.Execute(null);

        fixture.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public void Dispose_NeverConfirmed_DoesNotThrow()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture, "/import/pack.json");

        Should.NotThrow(viewModel.Dispose);
    }
}