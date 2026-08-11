using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Wizard;

/// <summary>
/// Tests unitaires purs (aucun <c>[AvaloniaFact]</c>) de la machine à étapes de
/// <see cref="WizardViewModel"/> : validation par étape, aperçu du slug, et création réellement
/// appelée sur <see cref="InstanceService"/> avec les bons arguments (<see cref="MockFileSystem"/>).
/// </summary>
public class WizardViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    private static (WizardViewModel ViewModel, InstanceService Service, RecordingOverlayService Overlay) CreateViewModel()
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var service = new InstanceService(repository, fileSystem, new FakeClock(Now));
        var overlay = new RecordingOverlayService();
        var viewModel = new WizardViewModel(service, overlay);
        return (viewModel, service, overlay);
    }

    [Fact]
    public void InitialState_StartsOnNameStepAndCannotGoBack()
    {
        var (viewModel, _, _) = CreateViewModel();

        viewModel.CurrentStepIndex.ShouldBe(0);
        viewModel.IsNameStep.ShouldBeTrue();
        viewModel.CanGoBack.ShouldBeFalse();
        viewModel.Steps.Count.ShouldBe(4);
        viewModel.Steps[0].IsCurrent.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Homestead 1.21", "homestead-1-21")]
    [InlineData("  Vintage Survival  ", "vintage-survival")]
    [InlineData("Été à la ferme", "ete-a-la-ferme")]
    public void SlugPreview_ReflectsNameLive(string name, string expectedSlug)
    {
        var (viewModel, _, _) = CreateViewModel();

        viewModel.Name = name;

        viewModel.SlugPreview.ShouldBe(expectedSlug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NameStep_BlankName_CannotGoNext(string blankName)
    {
        var (viewModel, _, _) = CreateViewModel();

        viewModel.Name = blankName;

        viewModel.CanGoNext.ShouldBeFalse();
        viewModel.NextCommand.CanExecute(null).ShouldBeFalse();
        viewModel.NameError.ShouldNotBeNull();
    }

    [Fact]
    public void NameStep_ValidName_CanGoNext()
    {
        var (viewModel, _, _) = CreateViewModel();

        viewModel.Name = "Homestead";

        viewModel.CanGoNext.ShouldBeTrue();
        viewModel.NameError.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("1.21")]
    public void VersionStep_InvalidFormat_CannotGoNext(string versionText)
    {
        var (viewModel, _, _) = CreateViewModel();
        viewModel.Name = "Homestead";
        viewModel.NextCommand.Execute(null);

        viewModel.VersionText = versionText;

        viewModel.IsVersionStep.ShouldBeTrue();
        viewModel.CanGoNext.ShouldBeFalse();
        viewModel.VersionError.ShouldNotBeNull();
    }

    [Fact]
    public void VersionStep_ValidFormat_CanGoNext()
    {
        var (viewModel, _, _) = CreateViewModel();
        viewModel.Name = "Homestead";
        viewModel.NextCommand.Execute(null);

        viewModel.VersionText = "1.21.3";

        viewModel.CanGoNext.ShouldBeTrue();
        viewModel.VersionError.ShouldBeNull();
    }

    [Fact]
    public void Next_DrivenThroughAllSteps_ReachesSummaryAsLastStep()
    {
        var (viewModel, _, _) = CreateViewModel();
        viewModel.Name = "Homestead";
        viewModel.NextCommand.Execute(null);
        viewModel.VersionText = "1.21.3";
        viewModel.NextCommand.Execute(null);
        viewModel.IsIconStep.ShouldBeTrue();
        viewModel.NextCommand.Execute(null);

        viewModel.IsSummaryStep.ShouldBeTrue();
        viewModel.IsLastStep.ShouldBeTrue();
        viewModel.Steps[0].IsDone.ShouldBeTrue();
        viewModel.Steps[3].IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public void Back_FromVersionStep_ReturnsToNameStepAndKeepsTypedName()
    {
        var (viewModel, _, _) = CreateViewModel();
        viewModel.Name = "Homestead";
        viewModel.NextCommand.Execute(null);

        viewModel.BackCommand.Execute(null);

        viewModel.IsNameStep.ShouldBeTrue();
        viewModel.Name.ShouldBe("Homestead");
    }

    [Fact]
    public void IconChoices_DefaultsToFirstEntrySelected()
    {
        var (viewModel, _, _) = CreateViewModel();

        viewModel.IconChoices.Count(choice => choice.IsSelected).ShouldBe(1);
        viewModel.IconChoices.First(choice => choice.IsSelected).Key.ShouldBe("default");
    }

    [Fact]
    public void IconChoice_SelectCommand_UpdatesSelectedIconKey()
    {
        var (viewModel, _, _) = CreateViewModel();

        viewModel.IconChoices.First(choice => choice.Key == "star").SelectCommand.Execute(null);

        viewModel.SelectedIconKey.ShouldBe("star");
        viewModel.IconChoices.First(choice => choice.Key == "star").IsSelected.ShouldBeTrue();
        viewModel.IconChoices.First(choice => choice.Key == "default").IsSelected.ShouldBeFalse();
    }

    private static void AdvanceToSummary(WizardViewModel viewModel, string name, string version)
    {
        viewModel.Name = name;
        viewModel.NextCommand.Execute(null);
        viewModel.VersionText = version;
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
    }

    [Fact]
    public async Task CreateAsync_ValidWizard_CallsInstanceServiceWithParsedNameAndVersion()
    {
        var (viewModel, _, overlay) = CreateViewModel();
        AdvanceToSummary(viewModel, "Homestead", "1.21.3");
        InstanceRecord? created = null;
        viewModel.Created += (_, record) => created = record;

        await viewModel.CreateCommand.ExecuteAsync(null);

        created.ShouldNotBeNull();
        created!.Metadata.Name.ShouldBe("Homestead");
        created.Metadata.GameVersion.ShouldBe(GameVersion.Parse("1.21.3"));
        created.Slug.ShouldBe("homestead");
        created.Metadata.Icon.ShouldBe(InstanceMetadata.DefaultIcon);
        overlay.Active.ShouldBeNull();
    }

    [Fact]
    public async Task CreateAsync_TrimsName()
    {
        var (viewModel, _, _) = CreateViewModel();
        AdvanceToSummary(viewModel, "  Homestead  ", "1.21.3");
        InstanceRecord? created = null;
        viewModel.Created += (_, record) => created = record;

        await viewModel.CreateCommand.ExecuteAsync(null);

        created.ShouldNotBeNull();
        created!.Metadata.Name.ShouldBe("Homestead");
    }

    [Fact]
    public async Task CreateAsync_NonDefaultIconChosen_PersistsBuiltinIcon()
    {
        var (viewModel, _, _) = CreateViewModel();
        viewModel.Name = "Homestead";
        viewModel.NextCommand.Execute(null);
        viewModel.VersionText = "1.21.3";
        viewModel.NextCommand.Execute(null);
        viewModel.IconChoices.First(choice => choice.Key == "package").SelectCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        InstanceRecord? created = null;
        viewModel.Created += (_, record) => created = record;

        await viewModel.CreateCommand.ExecuteAsync(null);

        created.ShouldNotBeNull();
        created!.Metadata.Icon.ShouldBe("builtin:package");
    }

    [Fact]
    public async Task CreateAsync_Success_ClosesOverlay()
    {
        var (viewModel, _, overlay) = CreateViewModel();
        AdvanceToSummary(viewModel, "Homestead", "1.21.3");
        overlay.Show(viewModel);

        await viewModel.CreateCommand.ExecuteAsync(null);

        overlay.Active.ShouldBeNull();
    }

    [Fact]
    public void CancelCommand_ClosesOverlayWithoutCreating()
    {
        var (viewModel, _, overlay) = CreateViewModel();
        overlay.Show(viewModel);

        viewModel.CancelCommand.Execute(null);

        overlay.Active.ShouldBeNull();
        overlay.Shown.ShouldNotBeEmpty();
    }
}