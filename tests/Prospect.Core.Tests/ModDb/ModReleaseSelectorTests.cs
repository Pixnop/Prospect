using Prospect.Core.Common;
using Prospect.Core.ModDb;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

public sealed class ModReleaseSelectorTests
{
    private static ModDbRelease Release(string version, int releaseId, params string[] gameVersions) => new()
    {
        ReleaseId = releaseId,
        FileId = releaseId * 2,
        ModIdString = "configlib",
        Version = ModVersion.Parse(version),
        FileName = $"configlib_{version}.zip",
        DownloadUrl = new Uri($"https://moddbcdn.vintagestory.at/configlib_{version}.zip"),
        CompatibleGameVersions = gameVersions.Select(GameVersion.Parse).ToArray(),
        CompatibleGameVersionTags = gameVersions,
    };

    [Fact]
    public void Select_ExactTag_PicksTheNewestReleaseCarryingIt()
    {
        ModDbRelease[] releases =
        [
            Release("1.9.0", 31002, "1.21.0", "1.21.3"),
            Release("1.11.1", 38314, "1.21.3", "1.22.0"),
            Release("1.12.0", 39980, "1.22.0", "1.22.1"),
        ];

        var choice = ModReleaseSelector.Select(releases, GameVersion.Parse("1.21.3")).ShouldNotBeNull();

        choice.Release.Version.ShouldBe(ModVersion.Parse("1.11.1"));
        choice.IsApproximate.ShouldBeFalse();
    }

    [Fact]
    public void Select_NewerReleaseWithoutTheExactTag_IsNotPickedInStrictMode()
    {
        // Le tag est du déclaratif d'auteur : sans la case cochée, rien n'affirme la compatibilité.
        ModDbRelease[] releases = [Release("1.12.0", 39980, "1.22.0"), Release("1.11.1", 38314, "1.21.3")];

        ModReleaseSelector.Select(releases, GameVersion.Parse("1.21.3"))!.Release.Version
            .ShouldBe(ModVersion.Parse("1.11.1"));
    }

    [Fact]
    public void Select_NoExactTagInStrictMode_FindsNothing()
    {
        ModDbRelease[] releases = [Release("1.12.0", 39980, "1.22.0", "1.22.1")];

        ModReleaseSelector.Select(releases, GameVersion.Parse("1.21.3")).ShouldBeNull();
    }

    [Fact]
    public void Select_WidenedToTheMinorSeries_FallsBackToAnotherPatchAndFlagsTheApproximation()
    {
        ModDbRelease[] releases = [Release("1.12.0", 39980, "1.21.0", "1.21.1")];

        var choice = ModReleaseSelector
            .Select(releases, GameVersion.Parse("1.21.3"), ModCompatibilityMode.WidenToMinorSeries)
            .ShouldNotBeNull();

        choice.Release.Version.ShouldBe(ModVersion.Parse("1.12.0"));
        choice.IsApproximate.ShouldBeTrue();
    }

    [Fact]
    public void Select_WidenedButAnExactTagExists_StillPrefersTheExactMatch()
    {
        ModDbRelease[] releases = [Release("1.12.0", 39980, "1.21.0"), Release("1.11.1", 38314, "1.21.3")];

        var choice = ModReleaseSelector
            .Select(releases, GameVersion.Parse("1.21.3"), ModCompatibilityMode.WidenToMinorSeries)
            .ShouldNotBeNull();

        choice.Release.Version.ShouldBe(ModVersion.Parse("1.11.1"));
        choice.IsApproximate.ShouldBeFalse();
    }

    [Fact]
    public void Select_WidenedAcrossAnotherMinorSeries_FindsNothing()
    {
        ModDbRelease[] releases = [Release("1.12.0", 39980, "1.20.4")];

        ModReleaseSelector
            .Select(releases, GameVersion.Parse("1.21.3"), ModCompatibilityMode.WidenToMinorSeries)
            .ShouldBeNull();
    }

    [Fact]
    public void Select_PrereleaseGameVersion_IsMatchedExactlyAndNotConfusedWithTheFinal()
    {
        ModDbRelease[] releases = [Release("1.12.0", 39980, "1.22.0-rc.1"), Release("1.11.1", 38314, "1.22.0")];

        ModReleaseSelector.Select(releases, GameVersion.Parse("1.22.0-rc.1"))!.Release.Version
            .ShouldBe(ModVersion.Parse("1.12.0"));
    }

    [Fact]
    public void Select_SameVersionPublishedTwice_KeepsTheLatestRelease()
    {
        ModDbRelease[] releases = [Release("1.12.0", 39980, "1.21.3"), Release("1.12.0", 41000, "1.21.3")];

        ModReleaseSelector.Select(releases, GameVersion.Parse("1.21.3"))!.Release.ReleaseId.ShouldBe(41000);
    }

    [Fact]
    public void Select_NoReleaseAtAll_FindsNothing()
        => ModReleaseSelector.Select([], GameVersion.Parse("1.21.3")).ShouldBeNull();

    [Theory]
    [InlineData("1.21.3", "1.21.0", true)]
    [InlineData("1.21.3", "1.21.3", true)]
    [InlineData("1.21.3", "1.22.0", false)]
    [InlineData("1.21.3", "2.21.3", false)]
    public void IsSameMinorSeries_ComparesMajorAndMinorOnly(string left, string right, bool expected)
        => ModReleaseSelector.IsSameMinorSeries(GameVersion.Parse(left), GameVersion.Parse(right)).ShouldBe(expected);

    // ── Liste complète des candidates (sélecteur de version du dialogue d'installation) ──────

    [Fact]
    public void SelectAll_ListsEveryTaggedRelease_NewestFirst()
    {
        ModDbRelease[] releases =
        [
            Release("1.9.0", 31002, "1.21.0", "1.21.3"),
            Release("1.12.0", 39980, "1.22.0"),
            Release("1.11.1", 38314, "1.21.3"),
        ];

        var candidates = ModReleaseSelector.SelectAll(releases, GameVersion.Parse("1.21.3"));

        candidates.Select(candidate => candidate.Release.Version.ToString())
            .ShouldBe(["1.11.1", "1.9.0"]);
        candidates.ShouldAllBe(candidate => !candidate.IsApproximate);
    }

    [Fact]
    public void SelectAll_FirstCandidate_IsExactlyWhatSelectWouldHavePicked()
    {
        // L'invariant qui permet au dialogue de présélectionner la première ligne sans refaire le
        // calcul, et qui garantit qu'installer sans toucher au sélecteur ne change RIEN au
        // comportement d'avant cette PR.
        ModDbRelease[] releases =
        [
            Release("1.12.0", 39980, "1.21.0"),
            Release("1.11.1", 38314, "1.21.3"),
            Release("1.9.0", 31002, "1.21.1"),
        ];

        foreach (var mode in Enum.GetValues<ModCompatibilityMode>())
        {
            var candidates = ModReleaseSelector.SelectAll(releases, GameVersion.Parse("1.21.3"), mode);
            var selected = ModReleaseSelector.Select(releases, GameVersion.Parse("1.21.3"), mode);

            (candidates.Count > 0 ? candidates[0] : null).ShouldBe(selected);
        }
    }

    [Fact]
    public void SelectAll_Widened_ListsTheExactMatchesBeforeTheApproximateOnes()
    {
        // Même arbitrage que Select : une compatibilité affirmée par l'auteur passe devant une
        // compatibilité supposée par nous, y compris quand l'approximative porte un numéro plus
        // élevé. C'est ce qui rend la présélection défendable.
        ModDbRelease[] releases =
        [
            Release("1.12.0", 39980, "1.21.0"),
            Release("1.11.1", 38314, "1.21.3"),
        ];

        var candidates = ModReleaseSelector.SelectAll(
            releases,
            GameVersion.Parse("1.21.3"),
            ModCompatibilityMode.WidenToMinorSeries);

        candidates.Count.ShouldBe(2);
        candidates[0].Release.Version.ShouldBe(ModVersion.Parse("1.11.1"));
        candidates[0].IsApproximate.ShouldBeFalse();
        candidates[1].Release.Version.ShouldBe(ModVersion.Parse("1.12.0"));
        candidates[1].IsApproximate.ShouldBeTrue();
    }

    [Fact]
    public void SelectAll_ReleaseTaggedBothExactlyAndInTheSeries_IsListedOnceAsExact()
    {
        ModDbRelease[] releases = [Release("1.12.0", 39980, "1.21.0", "1.21.3")];

        var candidates = ModReleaseSelector.SelectAll(
            releases,
            GameVersion.Parse("1.21.3"),
            ModCompatibilityMode.WidenToMinorSeries);

        candidates.ShouldHaveSingleItem().IsApproximate.ShouldBeFalse();
    }

    [Fact]
    public void SelectAll_NothingCompatible_GivesAnEmptyListRatherThanNull()
        => ModReleaseSelector.SelectAll([Release("1.12.0", 39980, "1.20.4")], GameVersion.Parse("1.21.3")).ShouldBeEmpty();
}