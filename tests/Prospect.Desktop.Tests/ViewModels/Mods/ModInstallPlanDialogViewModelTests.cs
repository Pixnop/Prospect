using Prospect.Core.Common;
using Prospect.Core.ModDb;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Mods;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Mods;

/// <summary>
/// Le dialogue de plan doit dire la VÉRITÉ sur une dépendance qu'il n'a pas su résoudre. Il n'avait
/// qu'un seul texte, « Introuvable sur le ModDB », servi indistinctement dans les deux cas — d'où le
/// mensonge relevé en conditions réelles sur <c>carryonlib</c>, dont la fiche existe bel et bien.
/// </summary>
public sealed class ModInstallPlanDialogViewModelTests
{
    private static ModInstallPlan PlanWith(string gameVersion, params UnresolvedModDependency[] unresolved)
    {
        var release = new ModDbRelease
        {
            ReleaseId = 50130,
            FileId = 109190,
            ModIdString = "carryon",
            Version = ModVersion.Parse("2.0.0-pre.8"),
            FileName = "CarryOn-2.0.0-pre.8.zip",
            DownloadUrl = new Uri("https://moddbcdn.vintagestory.at/carryon_2.0.0-pre.8.zip"),
            CompatibleGameVersions = [GameVersion.Parse(gameVersion)],
            CreatedUtc = null,
            Changelog = null,
        };

        var primary = new ModInstallItem(890, "Carry On", release, IsApproximateMatch: false, "carryon-2.0.0-pre.8.zip", 1024);

        return new ModInstallPlan(primary, [], [], unresolved, GameVersion.Parse(gameVersion));
    }

    private static ModInstallPlanDialogViewModel Dialog(ModInstallPlan plan)
        => new(plan, "Homestead 1.22", _ => Task.CompletedTask, new RecordingOverlayService());

    /// <summary>Le cas réel : la fiche existe, seules ses releases manquent pour cette version.</summary>
    [Fact]
    public void ADependencyPublishedWithoutACompatibleRelease_IsNeverCalledNotFound()
    {
        var dialog = Dialog(PlanWith(
            "1.22.6",
            new UnresolvedModDependency("carryonlib", ModDependencyResolution.NoCompatibleRelease, "CarryOnLib")));

        dialog.HasUnresolved.ShouldBeFalse("la fiche existe : rien à annoncer comme introuvable");
        dialog.UnresolvedMessage.ShouldBeEmpty();

        dialog.HasNoCompatibleRelease.ShouldBeTrue();
        dialog.NoCompatibleReleaseMessage.ShouldNotContain("Introuvable");

        // Le texte nomme la fiche ET la version du jeu : sans elle, l'utilisateur ne sait pas
        // pourquoi rien ne convient.
        dialog.NoCompatibleReleaseMessage.ShouldContain("CarryOnLib");
        dialog.NoCompatibleReleaseMessage.ShouldContain("1.22.6");
    }

    /// <summary>Et l'inverse : « introuvable » reste dit, mais seulement quand c'est vrai.</summary>
    [Fact]
    public void ADependencyThatTheModDbReallyDoesNotPublish_IsStillCalledNotFound()
    {
        var dialog = Dialog(PlanWith(
            "1.22.6",
            new UnresolvedModDependency("mod-fantome", ModDependencyResolution.NotOnModDb)));

        dialog.HasUnresolved.ShouldBeTrue();
        dialog.UnresolvedMessage.ShouldContain("Introuvable");
        dialog.UnresolvedMessage.ShouldContain("mod-fantome");

        dialog.HasNoCompatibleRelease.ShouldBeFalse();
        dialog.NoCompatibleReleaseMessage.ShouldBeEmpty();
    }

    /// <summary>Les deux verdicts peuvent coexister, et restent alors séparés à l'écran.</summary>
    [Fact]
    public void BothVerdictsAtOnce_AreReportedSeparately()
    {
        var dialog = Dialog(PlanWith(
            "1.22.6",
            new UnresolvedModDependency("mod-fantome", ModDependencyResolution.NotOnModDb),
            new UnresolvedModDependency("carryonlib", ModDependencyResolution.NoCompatibleRelease, "CarryOnLib")));

        dialog.HasUnresolved.ShouldBeTrue();
        dialog.HasNoCompatibleRelease.ShouldBeTrue();
        dialog.UnresolvedMessage.ShouldNotContain("CarryOnLib");
        dialog.NoCompatibleReleaseMessage.ShouldNotContain("mod-fantome");
    }

    [Fact]
    public void NoUnresolvedDependencyAtAll_ShowsNeitherBand()
    {
        var dialog = Dialog(PlanWith("1.22.6"));

        dialog.HasUnresolved.ShouldBeFalse();
        dialog.HasNoCompatibleRelease.ShouldBeFalse();
        dialog.Plan.NeedsConfirmation.ShouldBeFalse();
    }
}