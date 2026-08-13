using Prospect.Core.Diagnostics;
using Prospect.Core.ModDb;
using Prospect.Desktop.Formatting;

using Shouldly;

namespace Prospect.Desktop.Tests.Formatting;

/// <summary>
/// <see cref="ModLogBadgePresenter"/> : ce que la ligne d'un mod montre du dernier lancement. La
/// règle qui mérite d'être gardée est celle du SILENCE : un mod sain n'affiche rien, et une
/// intégration seulement possible (celle qu'un <c>dependsOn</c> conditionne, cible absente) reste
/// invisible : un mod de contenu en annonce des dizaines.
/// </summary>
public sealed class ModLogBadgePresenterTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private static InstalledMod Mod(string modId, string name) => new()
    {
        FilePath = $"/data/prospect/instances/survie/data/Mods/{modId}-1.0.0.zip",
        FileName = $"{modId}-1.0.0.zip",
        IsEnabled = true,
        Info = new ModInfo { ModId = modId, Name = name },
    };

    private static InstanceLogInsights Insights(
        IReadOnlyList<ModLogInsight>? mods = null,
        IReadOnlyList<ModIntegration>? integrations = null)
        => new(Noon, HasLog: true, mods ?? [], integrations ?? []);

    private static Dictionary<string, string> Names(params (string ModId, string Name)[] entries)
        => entries.ToDictionary(entry => entry.ModId, entry => entry.Name, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Describe_NothingKnown_ShowsNothing()
    {
        var badges = ModLogBadgePresenter.Describe(Mod("carryon", "Carry On"), InstanceLogInsights.None, Names());

        badges.ShouldBe(ModLogBadges.None);
        badges.HasErrors.ShouldBeFalse();
        badges.HasWarnings.ShouldBeFalse();
        badges.HasIntegration.ShouldBeFalse();
    }

    [Fact]
    public void Describe_ModWithErrors_CountsThemAndQuotesTheLines()
    {
        var insights = Insights([new ModLogInsight("carryon", 2, 1, ["première ligne", "deuxième ligne"])]);

        var badges = ModLogBadgePresenter.Describe(Mod("carryon", "Carry On"), insights, Names());

        badges.ErrorCount.ShouldBe(2);
        badges.WarningCount.ShouldBe(1);
        badges.HasErrors.ShouldBeTrue();
        badges.HasWarnings.ShouldBeTrue();
        badges.ProblemTooltip.ShouldContain("première ligne");
        badges.ProblemTooltip.ShouldContain("deuxième ligne");
    }

    [Fact]
    public void Describe_ResolvedIntegration_SaysItWorksWithTheOtherModByName()
    {
        var insights = Insights(integrations: [new ModIntegration("carryon", "bettercrates", ModIntegrationNature.Resolved, "assets/…")]);

        var badges = ModLogBadgePresenter.Describe(
            Mod("carryon", "Carry On"),
            insights,
            Names(("bettercrates", "Better Crates")));

        badges.IntegrationText.ShouldBe("fonctionne avec Better Crates");
        badges.IntegrationTooltip.ShouldContain("Better Crates");
    }

    [Fact]
    public void Describe_SeveralResolvedIntegrations_CountsTheOthers()
    {
        var insights = Insights(integrations:
        [
            new ModIntegration("carryon", "bettercrates", ModIntegrationNature.Resolved, "a"),
            new ModIntegration("carryon", "expandedfoods", ModIntegrationNature.Resolved, "b"),
            new ModIntegration("carryon", "hearthside", ModIntegrationNature.Resolved, "c"),
        ]);

        var badges = ModLogBadgePresenter.Describe(Mod("carryon", "Carry On"), insights, Names());

        badges.IntegrationText.ShouldBe("fonctionne avec bettercrates et 2 autres");
    }

    /// <summary>
    /// La cible n'est pas installée, donc son nom affichable n'existe pas : l'identifiant est
    /// alors la seule chose vraie qu'on puisse écrire.
    /// </summary>
    [Fact]
    public void Describe_MissingReference_SaysWhatTheModIsWaitingFor()
    {
        var insights = Insights(integrations: [new ModIntegration("hearthside", "saltyseas", ModIntegrationNature.Missing, "Patch 0 …")]);

        var badges = ModLogBadgePresenter.Describe(Mod("hearthside", "Hearthside"), insights, Names());

        badges.IntegrationText.ShouldBe("attend du contenu de saltyseas");
        badges.IntegrationTooltip.ShouldContain("absent au dernier lancement");
    }

    [Fact]
    public void Describe_ResolvedAndMissingTogether_LeadsWithWhatWorks()
    {
        var insights = Insights(integrations:
        [
            new ModIntegration("carryon", "saltyseas", ModIntegrationNature.Missing, "Patch 0 …"),
            new ModIntegration("carryon", "bettercrates", ModIntegrationNature.Resolved, "assets/…"),
        ]);

        var badges = ModLogBadgePresenter.Describe(
            Mod("carryon", "Carry On"),
            insights,
            Names(("bettercrates", "Better Crates")));

        badges.IntegrationText.ShouldBe("fonctionne avec Better Crates");
        badges.IntegrationTooltip.ShouldContain("saltyseas");
    }

    [Fact]
    public void Describe_OptionalIntegrationOnly_ShowsNothing()
    {
        var insights = Insights(integrations: [new ModIntegration("carryon", "bettercrates", ModIntegrationNature.Optional, "assets/…")]);

        ModLogBadgePresenter.Describe(Mod("carryon", "Carry On"), insights, Names()).ShouldBe(ModLogBadges.None);
    }

    [Fact]
    public void Describe_TooManyIntegrations_KeepsTheTooltipReadable()
    {
        var integrations = Enumerable
            .Range(0, ModLogBadgePresenter.MaxTooltipIntegrations + 4)
            .Select(index => new ModIntegration("carryon", $"mod{index:00}", ModIntegrationNature.Resolved, "assets/…"))
            .ToList();

        var badges = ModLogBadgePresenter.Describe(Mod("carryon", "Carry On"), Insights(integrations: integrations), Names());

        badges.IntegrationTooltip
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Length
            .ShouldBe(ModLogBadgePresenter.MaxTooltipIntegrations + 1);
    }

    [Fact]
    public void DisplayNames_IndexesInstalledModsByTheirIdentity()
    {
        var names = ModLogBadgePresenter.DisplayNames([Mod("carryon", "Carry On"), Mod("bettercrates", "Better Crates")]);

        names["carryon"].ShouldBe("Carry On");
        names["BETTERCRATES"].ShouldBe("Better Crates");
    }
}