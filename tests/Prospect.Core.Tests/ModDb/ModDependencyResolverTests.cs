using Prospect.Core.Common;
using Prospect.Core.ModDb;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

public sealed class ModDependencyResolverTests
{
    private static ModInfo Info(string modId, string? version = "1.0.0", params (string Id, string Constraint)[] dependencies) => new()
    {
        ModId = modId,
        Name = modId,
        Version = version is null ? null : ModVersion.Parse(version),
        Dependencies = dependencies.ToDictionary(
            entry => entry.Id,
            entry => VersionRequirement.Parse(entry.Constraint),
            StringComparer.OrdinalIgnoreCase),
    };

    private static InstalledMod Installed(ModInfo info, bool enabled = true) => new()
    {
        FilePath = $"/mods/{info.ModId}.zip",
        FileName = $"{info.ModId}.zip",
        IsEnabled = enabled,
        Info = info,
    };

    [Fact]
    public void FindUnsatisfied_DependencySatisfiedByAnInstalledMod_ReportsNothing()
    {
        var candidate = Info("configlib", "1.12.0", ("vsimgui", "1.2.0"));
        InstalledMod[] installed = [Installed(Info("vsimgui", "1.3.0"))];

        ModDependencyResolver.FindUnsatisfied(candidate, installed).ShouldBeEmpty();
    }

    [Fact]
    public void FindUnsatisfied_MissingDependency_IsDetectedLocallyFromTheDownloadedModInfo()
    {
        var candidate = Info("configlib", "1.12.0", ("vsimgui", "1.2.0"));

        var issue = ModDependencyResolver.FindUnsatisfied(candidate, []).ShouldHaveSingleItem();

        issue.ModIdString.ShouldBe("vsimgui");
        issue.Status.ShouldBe(ModDependencyStatus.Missing);
        issue.NeedsInstall.ShouldBeTrue();
        issue.ReportedByModDb.ShouldBeFalse();
    }

    [Fact]
    public void FindUnsatisfied_InstalledButTooOld_IsDetectedThroughTheMinimumBound()
    {
        var candidate = Info("configlib", "1.12.0", ("vsimgui", "1.2.0"));
        InstalledMod[] installed = [Installed(Info("vsimgui", "1.1.9"))];

        var issue = ModDependencyResolver.FindUnsatisfied(candidate, installed).ShouldHaveSingleItem();

        issue.Status.ShouldBe(ModDependencyStatus.TooOld);
        issue.InstalledVersion.ShouldBe(ModVersion.Parse("1.1.9"));
        issue.NeedsInstall.ShouldBeTrue();
    }

    [Fact]
    public void FindUnsatisfied_InstalledButDisabled_IsReportedWithoutProposingAReinstall()
    {
        // Réinstaller un mod déjà présent mais désactivé ne réglerait rien : c'est le toggle qu'il
        // faut, pas un téléchargement.
        var candidate = Info("configlib", "1.12.0", ("vsimgui", "1.2.0"));
        InstalledMod[] installed = [Installed(Info("vsimgui", "1.3.0"), enabled: false)];

        var issue = ModDependencyResolver.FindUnsatisfied(candidate, installed).ShouldHaveSingleItem();

        issue.Status.ShouldBe(ModDependencyStatus.Disabled);
        issue.NeedsInstall.ShouldBeFalse();
    }

    [Fact]
    public void FindUnsatisfied_SpecialIdentifiers_AreExcludedLikeTheModDbDoes()
    {
        var candidate = Info("extrainfo", "2.2.1", ("game", "1.22.0"), ("survival", ""), ("creative", ""));

        ModDependencyResolver.FindUnsatisfied(candidate, []).ShouldBeEmpty();
    }

    [Fact]
    public void FindUnsatisfied_WildcardConstraint_IsSatisfiedByAnyInstalledVersion()
    {
        var candidate = Info("x", "1.0.0", ("configlib", "*"));
        InstalledMod[] installed = [Installed(Info("configlib", "0.0.1"))];

        ModDependencyResolver.FindUnsatisfied(candidate, installed).ShouldBeEmpty();
    }

    [Fact]
    public void FindUnsatisfied_IdentifierOnlyReportedByTheModDb_StillSurfaces()
    {
        // resolve-deps voit les dépendances transitives, que le modinfo du mod demandé ne déclare
        // pas lui-même.
        var candidate = Info("configlib", "1.12.0");

        var issue = ModDependencyResolver.FindUnsatisfied(candidate, [], ["vsimgui"]).ShouldHaveSingleItem();

        issue.ModIdString.ShouldBe("vsimgui");
        issue.ReportedByModDb.ShouldBeTrue();
        issue.Requirement.IsAny.ShouldBeTrue();
    }

    [Fact]
    public void FindUnsatisfied_IdentifierReportedByBothSources_IsListedOnce()
    {
        var candidate = Info("configlib", "1.12.0", ("vsimgui", "1.2.0"));

        var issue = ModDependencyResolver.FindUnsatisfied(candidate, [], ["vsimgui"]).ShouldHaveSingleItem();

        issue.ReportedByModDb.ShouldBeTrue();
        issue.Requirement.IsAny.ShouldBeFalse();
    }

    [Fact]
    public void FindUnsatisfied_SpecialIdentifierReportedByTheModDb_IsStillIgnored()
        => ModDependencyResolver.FindUnsatisfied(Info("x"), [], ["game", "survival"]).ShouldBeEmpty();

    [Fact]
    public void FindUnsatisfied_UnreadableCandidateArchive_StillUsesTheModDbResolution()
    {
        // Une archive dont le modinfo est illisible n'annule pas la détection : le serveur reste
        // une source, même dégradée.
        var issues = ModDependencyResolver.FindUnsatisfied(candidate: null, [], ["vsimgui"]);

        issues.ShouldHaveSingleItem().ModIdString.ShouldBe("vsimgui");
    }

    [Fact]
    public void FindUnsatisfied_DependencyKnownOnlyThroughProvenance_CountsAsInstalled()
    {
        var candidate = Info("x", "1.0.0", ("vsimgui", ""));
        InstalledMod[] installed =
        [
            new()
            {
                FilePath = "/mods/vsimgui-1.3.0.zip",
                FileName = "vsimgui-1.3.0.zip",
                IsEnabled = true,
                Problem = ModInfoProblem.MissingModInfo,
                Provenance = new ModProvenance
                {
                    FileName = "vsimgui-1.3.0.zip",
                    ModId = 2000,
                    ModIdString = "vsimgui",
                    ReleaseId = 1,
                    FileId = 2,
                    Version = ModVersion.Parse("1.3.0"),
                    InstalledUtc = DateTimeOffset.UnixEpoch,
                },
            },
        ];

        ModDependencyResolver.FindUnsatisfied(candidate, installed).ShouldBeEmpty();
    }

    [Fact]
    public void Analyze_ListsSatisfiedDependenciesToo()
    {
        var candidate = Info("configlib", "1.12.0", ("vsimgui", "1.2.0"), ("missing", ""));
        InstalledMod[] installed = [Installed(Info("vsimgui", "1.3.0"))];

        var analysis = ModDependencyResolver.Analyze(candidate, installed);

        analysis.Count.ShouldBe(2);
        analysis.Single(issue => issue.ModIdString == "vsimgui").Status.ShouldBe(ModDependencyStatus.Satisfied);
    }

    // ── Vérification inverse, à la désinstallation ───────────────────────────────────

    [Fact]
    public void FindDependents_ModsDeclaringTheTarget_AreNamed()
    {
        var target = Installed(Info("vsimgui", "1.3.0"));
        InstalledMod[] installed =
        [
            target,
            Installed(Info("configlib", "1.12.0", ("vsimgui", "1.2.0"))),
            Installed(Info("extrainfo", "2.2.1", ("game", "1.22.0"))),
        ];

        var dependents = ModDependencyResolver.FindDependents(target, installed);

        dependents.ShouldHaveSingleItem().DisplayName.ShouldBe("configlib");
    }

    [Fact]
    public void FindDependents_NoOneDependsOnIt_IsEmpty()
    {
        var target = Installed(Info("standalone", "1.0.0"));

        ModDependencyResolver.FindDependents(target, [target]).ShouldBeEmpty();
    }

    [Fact]
    public void FindDependents_DisabledDependent_IsStillNamed()
    {
        // Un mod désactivé sera peut-être réactivé demain : le retrait le casserait quand même.
        var target = Installed(Info("vsimgui", "1.3.0"));
        InstalledMod[] installed = [target, Installed(Info("configlib", "1.12.0", ("vsimgui", "")), enabled: false)];

        ModDependencyResolver.FindDependents(target, installed).ShouldHaveSingleItem();
    }

    [Fact]
    public void FindDependents_UnidentifiedArchives_AreSkippedRatherThanGuessedAt()
    {
        var target = Installed(Info("vsimgui", "1.3.0"));
        InstalledMod[] installed =
        [
            target,
            new() { FilePath = "/mods/broken.zip", FileName = "broken.zip", IsEnabled = true, Problem = ModInfoProblem.UnreadableArchive },
        ];

        ModDependencyResolver.FindDependents(target, installed).ShouldBeEmpty();
    }

    [Fact]
    public void FindDependents_ResultsAreSortedByDisplayName()
    {
        var target = Installed(Info("vsimgui", "1.3.0"));
        InstalledMod[] installed =
        [
            target,
            Installed(Info("zeta", "1.0.0", ("vsimgui", ""))),
            Installed(Info("alpha", "1.0.0", ("vsimgui", ""))),
        ];

        ModDependencyResolver.FindDependents(target, installed).Select(mod => mod.DisplayName).ShouldBe(["alpha", "zeta"]);
    }
}