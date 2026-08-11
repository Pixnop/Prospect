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
}