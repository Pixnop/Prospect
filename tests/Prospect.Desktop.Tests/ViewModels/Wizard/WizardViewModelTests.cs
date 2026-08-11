using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Wizard;

/// <summary>
/// Tests unitaires purs (aucun <c>[AvaloniaFact]</c>) de la machine à étapes de
/// <see cref="WizardViewModel"/> : validation par étape, aperçu du slug, sélecteur de versions
/// branché sur le catalogue, et création réellement appelée sur <see cref="InstanceService"/> avec
/// les bons arguments (<see cref="MockFileSystem"/>).
/// </summary>
public class WizardViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        WizardViewModel ViewModel,
        RecordingOverlayService Overlay,
        FakeGameVersionCatalog Catalog,
        FakeDownloadManager Downloads,
        FakeGameInstallStrategy Strategy,
        IInstalledGameVersionRepository Versions,
        MockFileSystem FileSystem);

    private static Harness CreateHarness(params string[] installedVersions)
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var service = new InstanceService(repository, fileSystem, new FakeClock(Now));
        var overlay = new RecordingOverlayService();
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);

        foreach (var version in installedVersions)
        {
            var directory = fileSystem.Path.Combine(Paths.VersionsDirectory, version);
            fileSystem.AddFile(fileSystem.Path.Combine(directory, "Vintagestory"), new MockFileData("binaire"));
            fileSystem.AddFile(
                fileSystem.Path.Combine(directory, FileSystemInstalledGameVersionRepository.CompletionMarkerFileName),
                new MockFileData(version));
        }

        var catalog = new FakeGameVersionCatalog { Catalog = FakeGameVersionCatalog.Build("1.22.0-rc.1", "1.21.3", "1.20.4") };
        var downloads = new FakeDownloadManager();
        var strategy = new FakeGameInstallStrategy();
        var installService = new GameInstallService(catalog, downloads, versions, strategy);
        var viewModel = new WizardViewModel(service, overlay, catalog, versions, installService, new ImmediateUiDispatcher());

        return new Harness(viewModel, overlay, catalog, downloads, strategy, versions, fileSystem);
    }

    private static async Task<Harness> CreateLoadedHarnessAsync(params string[] installedVersions)
    {
        var harness = CreateHarness(installedVersions);
        await harness.ViewModel.LoadVersionsCommand.ExecuteAsync(null);

        return harness;
    }

    [Fact]
    public void InitialState_StartsOnNameStepAndCannotGoBack()
    {
        var harness = CreateHarness();

        harness.ViewModel.CurrentStepIndex.ShouldBe(0);
        harness.ViewModel.IsNameStep.ShouldBeTrue();
        harness.ViewModel.CanGoBack.ShouldBeFalse();
        harness.ViewModel.Steps.Count.ShouldBe(4);
        harness.ViewModel.Steps[0].IsCurrent.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Homestead 1.21", "homestead-1-21")]
    [InlineData("  Vintage Survival  ", "vintage-survival")]
    [InlineData("Été à la ferme", "ete-a-la-ferme")]
    public void SlugPreview_ReflectsNameLive(string name, string expectedSlug)
    {
        var harness = CreateHarness();

        harness.ViewModel.Name = name;

        harness.ViewModel.SlugPreview.ShouldBe(expectedSlug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NameStep_BlankName_CannotGoNext(string blankName)
    {
        var harness = CreateHarness();

        harness.ViewModel.Name = blankName;

        harness.ViewModel.CanGoNext.ShouldBeFalse();
        harness.ViewModel.NextCommand.CanExecute(null).ShouldBeFalse();
        harness.ViewModel.NameError.ShouldNotBeNull();
    }

    [Fact]
    public void NameStep_ValidName_CanGoNext()
    {
        var harness = CreateHarness();

        harness.ViewModel.Name = "Homestead";

        harness.ViewModel.CanGoNext.ShouldBeTrue();
        harness.ViewModel.NameError.ShouldBeNull();
    }

    [Fact]
    public async Task LoadVersions_ListsInstalledFirstThenDownloadable()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");

        harness.ViewModel.VersionChoices.Select(choice => choice.VersionText)
            .ShouldBe(["1.20.4", "1.22.0-rc.1", "1.21.3"]);
        harness.ViewModel.VersionChoices[0].IsInstalled.ShouldBeTrue();
        harness.ViewModel.VersionChoices[0].StateText.ShouldBe("installée");
        harness.ViewModel.VersionChoices[1].StateText.ShouldBe("590.5 MB à télécharger");
    }

    [Fact]
    public async Task LoadVersions_SelectsTheFirstChoiceSoTheStepIsImmediatelyValid()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");

        harness.ViewModel.SelectedVersion.ShouldNotBeNull();
        harness.ViewModel.SelectedVersion!.VersionText.ShouldBe("1.20.4");
        harness.ViewModel.IsVersionStepValid.ShouldBeTrue();
        harness.ViewModel.VersionChoices[0].IsSelected.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadVersions_CatalogUnreachable_KeepsTheInstalledOnesAndWarns()
    {
        var harness = CreateHarness("1.20.4");
        harness.Catalog.IsUnavailable = true;

        await harness.ViewModel.LoadVersionsCommand.ExecuteAsync(null);

        harness.ViewModel.VersionChoices.Select(choice => choice.VersionText).ShouldBe(["1.20.4"]);
        harness.ViewModel.VersionsWarning.ShouldNotBeNull();
    }

    [Fact]
    public async Task ChannelFilter_Stable_HidesReleaseCandidates()
    {
        var harness = await CreateLoadedHarnessAsync();

        harness.ViewModel.ChannelFilterIndex = 1;

        harness.ViewModel.VersionChoices.Select(choice => choice.VersionText).ShouldBe(["1.21.3", "1.20.4"]);
    }

    [Fact]
    public async Task ChannelFilter_Unstable_KeepsOnlyReleaseCandidates()
    {
        var harness = await CreateLoadedHarnessAsync();

        harness.ViewModel.ChannelFilterIndex = 2;

        harness.ViewModel.VersionChoices.Select(choice => choice.VersionText).ShouldBe(["1.22.0-rc.1"]);
    }

    [Fact]
    public async Task ChannelFilter_KeepsTheCurrentSelectionWhenItStillMatches()
    {
        var harness = await CreateLoadedHarnessAsync();
        harness.ViewModel.VersionChoices.First(choice => choice.VersionText == "1.20.4").SelectCommand.Execute(null);

        harness.ViewModel.ChannelFilterIndex = 1;

        harness.ViewModel.SelectedVersion!.VersionText.ShouldBe("1.20.4");
    }

    [Fact]
    public async Task VersionStep_WithoutAnyChoice_CannotGoNext()
    {
        var harness = CreateHarness();
        harness.Catalog.Catalog = FakeGameVersionCatalog.Build();
        await harness.ViewModel.LoadVersionsCommand.ExecuteAsync(null);
        harness.ViewModel.Name = "Homestead";
        harness.ViewModel.NextCommand.Execute(null);

        harness.ViewModel.IsVersionStep.ShouldBeTrue();
        harness.ViewModel.IsVersionStepValid.ShouldBeFalse();
        harness.ViewModel.CanGoNext.ShouldBeFalse();
    }

    [Fact]
    public async Task Next_DrivenThroughAllSteps_ReachesSummaryAsLastStep()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");
        harness.ViewModel.Name = "Homestead";
        harness.ViewModel.NextCommand.Execute(null);
        harness.ViewModel.NextCommand.Execute(null);
        harness.ViewModel.IsIconStep.ShouldBeTrue();
        harness.ViewModel.NextCommand.Execute(null);

        harness.ViewModel.IsSummaryStep.ShouldBeTrue();
        harness.ViewModel.IsLastStep.ShouldBeTrue();
        harness.ViewModel.Steps[0].IsDone.ShouldBeTrue();
        harness.ViewModel.Steps[3].IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public async Task SummaryNote_InstalledVersion_SaysNothingWillBeDownloaded()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");

        harness.ViewModel.SummaryNote.ShouldContain("déjà installée");
    }

    [Fact]
    public async Task SummaryNote_MissingVersion_AnnouncesTheDownload()
    {
        var harness = await CreateLoadedHarnessAsync();

        harness.ViewModel.SummaryNote.ShouldContain("téléchargée");
    }

    [Fact]
    public void Back_FromVersionStep_ReturnsToNameStepAndKeepsTypedName()
    {
        var harness = CreateHarness();
        harness.ViewModel.Name = "Homestead";
        harness.ViewModel.NextCommand.Execute(null);

        harness.ViewModel.BackCommand.Execute(null);

        harness.ViewModel.IsNameStep.ShouldBeTrue();
        harness.ViewModel.Name.ShouldBe("Homestead");
    }

    [Fact]
    public void IconChoices_DefaultsToFirstEntrySelected()
    {
        var harness = CreateHarness();

        harness.ViewModel.IconChoices.Count(choice => choice.IsSelected).ShouldBe(1);
        harness.ViewModel.IconChoices.First(choice => choice.IsSelected).Key.ShouldBe("default");
    }

    [Fact]
    public void IconChoice_SelectCommand_UpdatesSelectedIconKey()
    {
        var harness = CreateHarness();

        harness.ViewModel.IconChoices.First(choice => choice.Key == "star").SelectCommand.Execute(null);

        harness.ViewModel.SelectedIconKey.ShouldBe("star");
        harness.ViewModel.IconChoices.First(choice => choice.Key == "star").IsSelected.ShouldBeTrue();
        harness.ViewModel.IconChoices.First(choice => choice.Key == "default").IsSelected.ShouldBeFalse();
    }

    private static void AdvanceToSummary(WizardViewModel viewModel, string name, string version)
    {
        viewModel.Name = name;
        viewModel.NextCommand.Execute(null);
        viewModel.VersionChoices.First(choice => choice.VersionText == version).SelectCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
    }

    [Fact]
    public async Task CreateAsync_InstalledVersion_CreatesTheInstanceWithoutDownloadingAnything()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");
        AdvanceToSummary(harness.ViewModel, "Homestead", "1.20.4");
        InstanceRecord? created = null;
        harness.ViewModel.Created += (_, record) => created = record;

        await harness.ViewModel.CreateCommand.ExecuteAsync(null);

        created.ShouldNotBeNull();
        created!.Metadata.Name.ShouldBe("Homestead");
        created.Metadata.GameVersion.ShouldBe(GameVersion.Parse("1.20.4"));
        created.Slug.ShouldBe("homestead");
        created.Metadata.Icon.ShouldBe(InstanceMetadata.DefaultIcon);
        harness.Downloads.Requests.ShouldBeEmpty();
        harness.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public async Task CreateAsync_MissingVersion_InstallsItBeforeCreatingTheInstance()
    {
        var harness = await CreateLoadedHarnessAsync();
        AdvanceToSummary(harness.ViewModel, "Homestead", "1.21.3");
        InstanceRecord? created = null;
        harness.ViewModel.Created += (_, record) => created = record;

        await harness.ViewModel.CreateCommand.ExecuteAsync(null);

        harness.Downloads.Requests.ShouldHaveSingleItem();
        harness.Strategy.Installs.ShouldHaveSingleItem();
        harness.Versions.IsInstalled(GameVersion.Parse("1.21.3")).ShouldBeTrue();
        created.ShouldNotBeNull();
        created!.Metadata.GameVersion.ShouldBe(GameVersion.Parse("1.21.3"));
    }

    [Fact]
    public async Task CreateAsync_InstallFails_ReportsItAndDoesNotCreateAnyInstance()
    {
        var harness = await CreateLoadedHarnessAsync();
        harness.Strategy.Failure = new GameInstallFailedException("archive illisible");
        AdvanceToSummary(harness.ViewModel, "Homestead", "1.21.3");
        InstanceRecord? created = null;
        harness.ViewModel.Created += (_, record) => created = record;

        await harness.ViewModel.CreateCommand.ExecuteAsync(null);

        harness.ViewModel.CreateError.ShouldBe("archive illisible");
        created.ShouldBeNull();
        harness.ViewModel.IsCreating.ShouldBeFalse();
        harness.ViewModel.IsInstallingVersion.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateAsync_TrimsName()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");
        AdvanceToSummary(harness.ViewModel, "  Homestead  ", "1.20.4");
        InstanceRecord? created = null;
        harness.ViewModel.Created += (_, record) => created = record;

        await harness.ViewModel.CreateCommand.ExecuteAsync(null);

        created.ShouldNotBeNull();
        created!.Metadata.Name.ShouldBe("Homestead");
    }

    [Fact]
    public async Task CreateAsync_NonDefaultIconChosen_PersistsBuiltinIcon()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");
        harness.ViewModel.Name = "Homestead";
        harness.ViewModel.NextCommand.Execute(null);
        harness.ViewModel.VersionChoices.First(choice => choice.VersionText == "1.20.4").SelectCommand.Execute(null);
        harness.ViewModel.NextCommand.Execute(null);
        harness.ViewModel.IconChoices.First(choice => choice.Key == "package").SelectCommand.Execute(null);
        harness.ViewModel.NextCommand.Execute(null);
        InstanceRecord? created = null;
        harness.ViewModel.Created += (_, record) => created = record;

        await harness.ViewModel.CreateCommand.ExecuteAsync(null);

        created.ShouldNotBeNull();
        created!.Metadata.Icon.ShouldBe("builtin:package");
    }

    [Fact]
    public async Task CreateAsync_Success_ClosesOverlay()
    {
        var harness = await CreateLoadedHarnessAsync("1.20.4");
        AdvanceToSummary(harness.ViewModel, "Homestead", "1.20.4");
        harness.Overlay.Show(harness.ViewModel);

        await harness.ViewModel.CreateCommand.ExecuteAsync(null);

        harness.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public void CancelCommand_ClosesOverlayWithoutCreating()
    {
        var harness = CreateHarness();
        harness.Overlay.Show(harness.ViewModel);

        harness.ViewModel.CancelCommand.Execute(null);

        harness.Overlay.Active.ShouldBeNull();
        harness.Overlay.Shown.ShouldNotBeEmpty();
    }

    [Fact]
    public void Report_DownloadPhase_ShowsTheProgressAndItsDetail()
    {
        var harness = CreateHarness();

        harness.ViewModel.Report(new GameInstallProgress(GameInstallPhase.Downloading, 0.5d, 500_000, 1_000_000, 250_000));

        harness.ViewModel.InstallPhaseText.ShouldBe("Téléchargement");
        harness.ViewModel.InstallProgressPercent.ShouldBe(50d);
        harness.ViewModel.IsInstallIndeterminate.ShouldBeFalse();
        harness.ViewModel.InstallDetailText.ShouldBe("500.0 KB / 1.0 MB · 250.0 KB/s");
    }

    [Fact]
    public void Report_InstallPhase_HasNoMeasurableRatio()
    {
        var harness = CreateHarness();

        harness.ViewModel.Report(GameInstallProgress.ForPhase(GameInstallPhase.Installing));

        harness.ViewModel.InstallPhaseText.ShouldBe("Installation");
        harness.ViewModel.IsInstallIndeterminate.ShouldBeTrue();
        harness.ViewModel.InstallDetailText.ShouldBeEmpty();
    }
}