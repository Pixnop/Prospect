using Prospect.Core.Common;
using Prospect.Core.Diagnostics;
using Prospect.Core.ModDb;
using Prospect.Core.Runtime;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Dialogs;

/// <summary>
/// <see cref="InstanceDoctorDialogViewModel"/> : mise en lignes/groupes d'un
/// <see cref="InstanceDoctorReport"/> déjà calculé — ce dialogue ne fait aucun diagnostic lui-même
/// (voir <c>Prospect.Core.Tests.Diagnostics.InstanceDoctorTests</c> pour les vérifications elles-mêmes),
/// seulement la présentation : groupement par sévérité, action câblée par ligne, état « tout va bien ».
/// </summary>
public sealed class InstanceDoctorDialogViewModelTests
{
    private static readonly GameVersion Version = GameVersion.Parse("1.22.1");

    private static InstanceDoctorReport HealthyReport() => new(
        new GameVersionDoctorResult(GameVersionDoctorStatus.Installed, Version),
        RuntimeCheckResult.Present(GameRuntimeRequirement.Known("Microsoft.NETCore.App", new Version(8, 0, 10))),
        [],
        new ModCompatibilityDoctorResult(ConfirmedCount: 1, ApproximateCount: 0, UnknownCount: 0, TotalChecked: 1),
        new DiskSpaceDoctorResult(AvailableBytes: 100L * 1024 * 1024 * 1024, ThresholdBytes: InstanceDoctor.LowDiskSpaceThresholdBytes));

    private static InstanceDoctorDialogViewModel Create(
        InstanceDoctorReport report,
        Action? navigateToVersions = null,
        Action? openModsTab = null,
        RecordingOverlayService? overlay = null)
        => new(report, navigateToVersions ?? (() => { }), openModsTab ?? (() => { }), overlay ?? new RecordingOverlayService());

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var overlay = new RecordingOverlayService();

        Should.Throw<ArgumentNullException>(() => new InstanceDoctorDialogViewModel(null!, () => { }, () => { }, overlay));
        Should.Throw<ArgumentNullException>(() => new InstanceDoctorDialogViewModel(HealthyReport(), null!, () => { }, overlay));
        Should.Throw<ArgumentNullException>(() => new InstanceDoctorDialogViewModel(HealthyReport(), () => { }, null!, overlay));
        Should.Throw<ArgumentNullException>(() => new InstanceDoctorDialogViewModel(HealthyReport(), () => { }, () => { }, null!));
    }

    [Fact]
    public void AllChecksHealthy_IsAllClearWithNoGroups()
    {
        var dialog = Create(HealthyReport());

        dialog.IsAllClear.ShouldBeTrue();
        dialog.Groups.ShouldBeEmpty();
    }

    [Fact]
    public void GameVersionMissing_ErrorRowProposesInstallAndNavigates()
    {
        var report = HealthyReport() with { GameVersion = new GameVersionDoctorResult(GameVersionDoctorStatus.Missing, Version) };
        var navigated = false;
        var dialog = Create(report, navigateToVersions: () => navigated = true);

        var row = dialog.Groups.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        row.Severity.ShouldBe(InstanceDoctorSeverity.Error);
        row.Message.ShouldContain("1.22.1");
        row.ActionLabel.ShouldBe("Installer");
        row.HasAction.ShouldBeTrue();

        row.ActionCommand!.Execute(null);
        navigated.ShouldBeTrue();
    }

    [Fact]
    public void GameVersionIncomplete_ErrorRowProposesReinstall()
    {
        var report = HealthyReport() with { GameVersion = new GameVersionDoctorResult(GameVersionDoctorStatus.Incomplete, Version) };
        var dialog = Create(report);

        var row = dialog.Groups.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        row.ActionLabel.ShouldBe("Réinstaller");
    }

    [Fact]
    public void RuntimeMissing_ErrorRowNamesTheExactFrameworkAndVersion()
    {
        var requirement = GameRuntimeRequirement.Known("Microsoft.NETCore.App", new Version(10, 0, 0));
        var report = HealthyReport() with { Runtime = RuntimeCheckResult.Missing(requirement) };
        var dialog = Create(report);

        var row = dialog.Groups.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        row.Severity.ShouldBe(InstanceDoctorSeverity.Error);
        row.Message.ShouldContain("Microsoft.NETCore.App");
        row.Message.ShouldContain("10.0.0");
        row.HasAction.ShouldBeFalse();
    }

    [Fact]
    public void RuntimeIndeterminate_IsAWarningRatherThanAnErrorOrSilence()
    {
        var report = HealthyReport() with { Runtime = RuntimeCheckResult.Indeterminate };
        var dialog = Create(report);

        var row = dialog.Groups.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        row.Severity.ShouldBe(InstanceDoctorSeverity.Warning);
    }

    [Fact]
    public void ModIssues_EachProducesItsOwnRowWithTheOpenModsAction()
    {
        var dependency = new ModDependencyIssue("vsimgui", VersionRequirement.Parse("1.0.0"), ModDependencyStatus.Missing, null, false);
        var issues = new[]
        {
            new ModDoctorIssue(ModDoctorIssueKind.UnsatisfiedDependency, "Config lib", Dependency: dependency),
            new ModDoctorIssue(ModDoctorIssueKind.Unidentified, "mystere.zip", Problem: ModInfoProblem.MissingModInfo),
        };
        var report = HealthyReport() with { ModIssues = issues };
        var opened = false;
        var dialog = Create(report, openModsTab: () => opened = true);

        var errorGroup = dialog.Groups.Single(group => group.Title.Contains("corriger"));
        var warningGroup = dialog.Groups.Single(group => group.Title.Contains("surveiller"));
        var dependencyRow = errorGroup.Rows.ShouldHaveSingleItem();
        var unidentifiedRow = warningGroup.Rows.ShouldHaveSingleItem();

        dependencyRow.Message.ShouldContain("Config lib");
        dependencyRow.Message.ShouldContain("vsimgui");
        dependencyRow.ActionLabel.ShouldBe("Voir les mods");

        unidentifiedRow.Message.ShouldContain("mystere.zip");
        unidentifiedRow.ActionLabel.ShouldBe("Voir les mods");

        dependencyRow.ActionCommand!.Execute(null);
        opened.ShouldBeTrue();
    }

    [Fact]
    public void ModCompatibilityWhollyUnknown_UsesTheExactPhraseRatherThanInventingAVerdict()
    {
        var report = HealthyReport() with
        {
            ModCompatibility = new ModCompatibilityDoctorResult(ConfirmedCount: 0, ApproximateCount: 0, UnknownCount: 1, TotalChecked: 1),
        };
        var dialog = Create(report);

        var row = dialog.Groups.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        row.Severity.ShouldBe(InstanceDoctorSeverity.Warning);
        row.Message.ShouldBe("Compatibilité de version de jeu inconnue : lance une vérification des mises à jour pour en savoir plus.");
    }

    [Fact]
    public void ModCompatibilityPartiallyApproximate_NamesHowManyModsAreUncertain()
    {
        var report = HealthyReport() with
        {
            ModCompatibility = new ModCompatibilityDoctorResult(ConfirmedCount: 2, ApproximateCount: 1, UnknownCount: 0, TotalChecked: 3),
        };
        var dialog = Create(report);

        var row = dialog.Groups.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        row.Message.ShouldContain("1 mod");
        row.Message.ShouldContain("1.22.1");
    }

    [Fact]
    public void DiskSpaceLow_WarningRowShowsTheFormattedAmountLeft()
    {
        // ByteSizeFormatter travaille en base 1000 (docstring de la classe) : 512 000 000 octets
        // rendent exactement « 512.0 MB », pas 512 * 1024 * 1024 qui rendrait 536.9 MB.
        var report = HealthyReport() with { DiskSpace = new DiskSpaceDoctorResult(512_000_000L, InstanceDoctor.LowDiskSpaceThresholdBytes) };
        var dialog = Create(report);

        var row = dialog.Groups.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        row.Severity.ShouldBe(InstanceDoctorSeverity.Warning);
        row.Message.ShouldContain("512.0 MB");
        row.HasAction.ShouldBeFalse();
    }

    [Fact]
    public void MixOfErrorsAndWarnings_GroupsErrorsBeforeWarningsWithCountingTitles()
    {
        var report = HealthyReport() with
        {
            GameVersion = new GameVersionDoctorResult(GameVersionDoctorStatus.Missing, Version),
            Runtime = RuntimeCheckResult.Indeterminate,
        };
        var dialog = Create(report);

        dialog.IsAllClear.ShouldBeFalse();
        dialog.Groups.Count.ShouldBe(2);
        dialog.Groups[0].Title.ShouldBe("1 point à corriger");
        dialog.Groups[0].Rows.ShouldAllBe(row => row.Severity == InstanceDoctorSeverity.Error);
        dialog.Groups[1].Title.ShouldBe("1 point à surveiller");
        dialog.Groups[1].Rows.ShouldAllBe(row => row.Severity == InstanceDoctorSeverity.Warning);
    }

    [Fact]
    public void Close_ClosesTheOverlay()
    {
        var overlay = new RecordingOverlayService();
        var dialog = Create(HealthyReport(), overlay: overlay);
        overlay.Show(dialog);

        dialog.CloseCommand.Execute(null);

        overlay.Active.ShouldBeNull();
    }
}