using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Migration;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Migration;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Migration;

/// <summary>
/// <see cref="AdoptVslViewModel"/> : sélection par défaut, progression, rapport groupé par
/// catégorie. Mêmes doubles de test que le reste des dialogues (<see cref="RecordingOverlayService"/>,
/// <see cref="RecordingToastService"/>, <see cref="ImmediateUiDispatcher"/>).
/// </summary>
public sealed class AdoptVslViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        VslAdoptionService AdoptionService,
        IInstalledGameVersionRepository GameVersions,
        RecordingOverlayService Overlay,
        RecordingToastService Toasts,
        MockFileSystem FileSystem);

    private static Fixture CreateServices()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var store = new JsonFileStore(fileSystem);
        var instanceRepository = new FileSystemInstanceRepository(fileSystem, Paths, store, new InstanceMetadataMigrationPipeline([]));
        var instanceService = new InstanceService(instanceRepository, fileSystem, clock);
        var gameVersions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var adoptionService = new VslAdoptionService(instanceService, instanceRepository, gameVersions, store, fileSystem, clock);

        return new Fixture(adoptionService, gameVersions, new RecordingOverlayService(), new RecordingToastService(), fileSystem);
    }

    private static AdoptVslViewModel CreateViewModel(Fixture fixture, VslDetectionResult detection)
        => new(detection, fixture.AdoptionService, fixture.GameVersions, fixture.FileSystem, fixture.Overlay, fixture.Toasts, new ImmediateUiDispatcher());

    private static VslInstallation Installation(string name = "Survie", string path = "/vsl/a", string version = "1.20.4")
        => new() { Id = "id-" + name, Name = name, Path = path, Version = version };

    private static VslDetectionResult Detected(
        IReadOnlyList<VslInstallation>? installations = null,
        IReadOnlyList<VslGameVersionEntry>? gameVersions = null)
        => new()
        {
            IsDetected = true,
            RootDirectory = "/home/pixnop/.config",
            HasConfigFile = true,
            Installations = installations ?? [],
            GameVersions = gameVersions ?? [],
        };

    private static void SeedFolder(MockFileSystem fileSystem, string path)
        => fileSystem.AddFile(fileSystem.Path.Combine(path, "Mods", "a.zip"), new MockFileData("mod"));

    // ── Construction / sélection par défaut ─────────────────────────────────────────

    [Fact]
    public void Constructor_Installations_AreAllSelectedByDefault()
    {
        var fixture = CreateServices();
        var installation = Installation();
        SeedFolder(fixture.FileSystem, installation.Path);

        var viewModel = CreateViewModel(fixture, Detected(installations: [installation]));

        viewModel.InstallationRows.ShouldHaveSingleItem().IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_EngineNotInstalled_IsSelectedByDefault()
    {
        var fixture = CreateServices();
        var engine = new VslGameVersionEntry { Version = "1.20.4", Path = "/vsl/engine" };

        var viewModel = CreateViewModel(fixture, Detected(gameVersions: [engine]));

        viewModel.EngineRows.ShouldHaveSingleItem().IsSelected.ShouldBeTrue();
    }

    [Fact]
    public async Task Constructor_EngineAlreadyInstalled_IsNotSelectedByDefault()
    {
        var fixture = CreateServices();
        await fixture.GameVersions.MarkCompleteAsync(GameVersion.Parse("1.20.4"), CancellationToken.None);
        var engine = new VslGameVersionEntry { Version = "1.20.4", Path = "/vsl/engine" };

        var viewModel = CreateViewModel(fixture, Detected(gameVersions: [engine]));

        var row = viewModel.EngineRows.ShouldHaveSingleItem();
        row.IsSelected.ShouldBeFalse();
        row.IsAlreadyInstalled.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_InstallationRow_CountsModsAndComputesSize()
    {
        var fixture = CreateServices();
        var installation = Installation();
        fixture.FileSystem.AddFile(fixture.FileSystem.Path.Combine(installation.Path, "Mods", "a.zip"), new MockFileData(new byte[100]));
        fixture.FileSystem.AddFile(fixture.FileSystem.Path.Combine(installation.Path, "Mods", "b.zip"), new MockFileData(new byte[100]));
        fixture.FileSystem.AddFile(fixture.FileSystem.Path.Combine(installation.Path, "Saves", "world.vcdbs"), new MockFileData(new byte[300]));

        var viewModel = CreateViewModel(fixture, Detected(installations: [installation]));

        var row = viewModel.InstallationRows.ShouldHaveSingleItem();
        row.ModCountText.ShouldBe("2 mods");
        row.SizeText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Constructor_NoInstallationsOrEngines_HasNoContentFlags()
    {
        var fixture = CreateServices();

        var viewModel = CreateViewModel(fixture, Detected());

        viewModel.HasInstallations.ShouldBeFalse();
        viewModel.HasEngines.ShouldBeFalse();
    }

    // ── Confirmation ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmAsync_SelectedInstallation_CreatesInstanceAndShowsReport()
    {
        var fixture = CreateServices();
        var installation = Installation();
        SeedFolder(fixture.FileSystem, installation.Path);
        var viewModel = CreateViewModel(fixture, Detected(installations: [installation]));

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        viewModel.Step.ShouldBe(AdoptVslStep.Report);
        viewModel.ReportGroups.ShouldContain(group => group.Rows.Any(row => row.Label == "Survie"));
    }

    [Fact]
    public async Task ConfirmAsync_UncheckedInstallation_IsNotSubmitted()
    {
        var fixture = CreateServices();
        var installation = Installation();
        SeedFolder(fixture.FileSystem, installation.Path);
        var viewModel = CreateViewModel(fixture, Detected(installations: [installation]));
        viewModel.InstallationRows[0].IsSelected = false;

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        // Rien de sélectionné : le rapport ne contient donc aucun groupe (BuildReport n'ajoute un
        // groupe que s'il a au moins une ligne), preuve indirecte mais fiable que rien n'a été
        // soumis au service d'adoption.
        viewModel.ReportGroups.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfirmAsync_Success_RaisesCompletedEvent()
    {
        var fixture = CreateServices();
        var installation = Installation();
        SeedFolder(fixture.FileSystem, installation.Path);
        var viewModel = CreateViewModel(fixture, Detected(installations: [installation]));
        VslAdoptionOutcome? raised = null;
        viewModel.Completed += (_, outcome) => raised = outcome;

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        raised.ShouldNotBeNull();
        raised!.AdoptedInstallationCount.ShouldBe(1);
    }

    [Fact]
    public async Task ConfirmAsync_Success_ShowsSuccessToast()
    {
        var fixture = CreateServices();
        var installation = Installation();
        SeedFolder(fixture.FileSystem, installation.Path);
        var viewModel = CreateViewModel(fixture, Detected(installations: [installation]));

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        fixture.Toasts.Shown.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ConfirmAsync_SkippedInstallation_AppearsInASkippedGroupWithReason()
    {
        var fixture = CreateServices();
        var installation = Installation(version: "n/a");
        SeedFolder(fixture.FileSystem, installation.Path);
        var viewModel = CreateViewModel(fixture, Detected(installations: [installation]));

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        var row = viewModel.ReportGroups.SelectMany(group => group.Rows).ShouldHaveSingleItem();
        row.Label.ShouldBe("Survie");
        row.DetailText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ConfirmAsync_ReportsProgressDuringAdoption()
    {
        var fixture = CreateServices();
        var installation = Installation();
        SeedFolder(fixture.FileSystem, installation.Path);
        var viewModel = CreateViewModel(fixture, Detected(installations: [installation]));
        var phaseTextsSeen = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AdoptVslViewModel.ProgressPhaseText))
            {
                phaseTextsSeen.Add(viewModel.ProgressPhaseText);
            }
        };

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        phaseTextsSeen.ShouldNotBeEmpty();
    }

    // ── Fermeture ────────────────────────────────────────────────────────────────────

    [Fact]
    public void CancelSelection_ClosesTheOverlay()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture, Detected());
        fixture.Overlay.Show(viewModel);

        viewModel.CancelSelectionCommand.Execute(null);

        fixture.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public async Task CloseReport_AfterSuccess_ClosesTheOverlay()
    {
        var fixture = CreateServices();
        var installation = Installation();
        SeedFolder(fixture.FileSystem, installation.Path);
        var viewModel = CreateViewModel(fixture, Detected(installations: [installation]));
        fixture.Overlay.Show(viewModel);
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        viewModel.CloseReportCommand.Execute(null);

        fixture.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public void Dispose_DoesNotThrowWhenNoAdoptionIsInFlight()
    {
        var fixture = CreateServices();
        var viewModel = CreateViewModel(fixture, Detected());

        Should.NotThrow(viewModel.Dispose);
    }
}