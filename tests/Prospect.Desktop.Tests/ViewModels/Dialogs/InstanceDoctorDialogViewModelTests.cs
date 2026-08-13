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
        RecordingOverlayService? overlay = null,
        Func<string, Task>? installMod = null)
        => new(
            report,
            navigateToVersions ?? (() => { }),
            openModsTab ?? (() => { }),
            installMod ?? (_ => Task.CompletedTask),
            overlay ?? new RecordingOverlayService());

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var overlay = new RecordingOverlayService();

        Should.Throw<ArgumentNullException>(() => new InstanceDoctorDialogViewModel(null!, () => { }, () => { }, _ => Task.CompletedTask, overlay));
        Should.Throw<ArgumentNullException>(() => new InstanceDoctorDialogViewModel(HealthyReport(), null!, () => { }, _ => Task.CompletedTask, overlay));
        Should.Throw<ArgumentNullException>(() => new InstanceDoctorDialogViewModel(HealthyReport(), () => { }, null!, _ => Task.CompletedTask, overlay));
        Should.Throw<ArgumentNullException>(() => new InstanceDoctorDialogViewModel(HealthyReport(), () => { }, () => { }, null!, overlay));
        Should.Throw<ArgumentNullException>(() => new InstanceDoctorDialogViewModel(HealthyReport(), () => { }, () => { }, _ => Task.CompletedTask, null!));
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

    /// <summary>
    /// « Voir les mods » sur une dépendance MANQUANTE renvoyait l'utilisateur regarder une liste où
    /// le mod manquant, par définition, n'est pas. L'action nomme donc le mod et mène au plan
    /// d'installation. Le cas de l'archive non identifiée, lui, garde « Voir les mods » : c'est bien
    /// là qu'on va la retirer.
    /// </summary>
    [Fact]
    public void AMissingDependency_OffersToInstallItByName()
    {
        var dependency = new ModDependencyIssue("carryonlib", VersionRequirement.Parse("1.0.0"), ModDependencyStatus.Missing, null, false);
        var issues = new[]
        {
            new ModDoctorIssue(ModDoctorIssueKind.UnsatisfiedDependency, "Carry On", Dependency: dependency),
            new ModDoctorIssue(ModDoctorIssueKind.Unidentified, "mystere.zip", Problem: ModInfoProblem.MissingModInfo),
        };
        var report = HealthyReport() with { ModIssues = issues };
        var opened = false;
        string? requested = null;
        var dialog = Create(
            report,
            openModsTab: () => opened = true,
            installMod: identifier =>
            {
                requested = identifier;

                return Task.CompletedTask;
            });

        var errorGroup = dialog.Groups.Single(group => group.Title.Contains("corriger"));
        var warningGroup = dialog.Groups.Single(group => group.Title.Contains("surveiller"));
        var dependencyRow = errorGroup.Rows.ShouldHaveSingleItem();
        var unidentifiedRow = warningGroup.Rows.ShouldHaveSingleItem();

        dependencyRow.Message.ShouldContain("Carry On");
        dependencyRow.Message.ShouldContain("carryonlib");
        dependencyRow.ActionLabel.ShouldBe("Installer « carryonlib »…");

        unidentifiedRow.Message.ShouldContain("mystere.zip");
        unidentifiedRow.ActionLabel.ShouldBe("Voir les mods");

        dependencyRow.ActionCommand!.Execute(null);
        requested.ShouldBe("carryonlib");
        opened.ShouldBeFalse();

        unidentifiedRow.ActionCommand!.Execute(null);
        opened.ShouldBeTrue();
    }

    /// <summary>Une dépendance présente mais trop ancienne mène au même plan, sous le verbe qui convient.</summary>
    [Fact]
    public void ADependencyTooOld_OffersToUpdateItByName()
    {
        var dependency = new ModDependencyIssue(
            "configlib",
            VersionRequirement.Parse("1.11.0"),
            ModDependencyStatus.TooOld,
            ModVersion.Parse("1.0.0"),
            ReportedByModDb: false);
        var report = HealthyReport() with
        {
            ModIssues = [new ModDoctorIssue(ModDoctorIssueKind.UnsatisfiedDependency, "Carry On", Dependency: dependency)],
        };
        string? requested = null;
        var dialog = Create(report, installMod: identifier =>
        {
            requested = identifier;

            return Task.CompletedTask;
        });

        var row = dialog.Groups.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        row.ActionLabel.ShouldBe("Mettre à jour « configlib »…");

        row.ActionCommand!.Execute(null);
        requested.ShouldBe("configlib");
    }

    /// <summary>
    /// Une dépendance simplement DÉSACTIVÉE n'a rien à télécharger : le zip est là, il dort. Son
    /// action reste « Voir les mods », qui est exactement l'endroit où le réveiller.
    /// </summary>
    [Fact]
    public void ADisabledDependency_KeepsTheSeeTheModsAction()
    {
        var dependency = new ModDependencyIssue("configlib", VersionRequirement.Parse("*"), ModDependencyStatus.Disabled, ModVersion.Parse("1.11.1"), false);
        var report = HealthyReport() with
        {
            ModIssues = [new ModDoctorIssue(ModDoctorIssueKind.UnsatisfiedDependency, "Carry On", Dependency: dependency)],
        };
        var opened = false;
        var dialog = Create(report, openModsTab: () => opened = true);

        var row = dialog.Groups.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        row.ActionLabel.ShouldBe("Voir les mods");

        row.ActionCommand!.Execute(null);
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