using Prospect.Core.Common;

using Shouldly;

namespace Prospect.Core.Tests.Common;

/// <summary>
/// Couvre <see cref="VersionRequirement"/> avec les échantillons réels de
/// docs/research/moddb-api.md : dependencies vides (JSON Patches lib), une dépendance mod
/// (Config lib → vsimgui 1.2.0), une dépendance de jeu (Extra Info → game 1.22.0).
/// </summary>
public class VersionRequirementTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("   ")]
    public void Parse_NullOrEmptyOrWildcard_AcceptsAnyVersion(string? raw)
    {
        var requirement = VersionRequirement.Parse(raw);

        requirement.IsAny.ShouldBeTrue();
        requirement.IsSatisfiedBy(ModVersion.Parse("0.0.1")).ShouldBeTrue();
        requirement.IsSatisfiedBy(ModVersion.Parse("99.99.99")).ShouldBeTrue();
        requirement.IsSatisfiedBy(GameVersion.Parse("1.9.14")).ShouldBeTrue();
    }

    [Fact]
    public void TryParse_NullValue_ReturnsTrueAsAnyVersion()
    {
        var parsed = VersionRequirement.TryParse(null, out var requirement);

        parsed.ShouldBeTrue();
        requirement.IsAny.ShouldBeTrue();
    }

    [Theory]
    [InlineData("1.2.0", "1.2.0", true)]
    [InlineData("1.2.0", "1.3.0", true)]
    [InlineData("1.2.0", "1.1.9", false)]
    [InlineData("1.8.0", "1.8.0", true)]
    public void IsSatisfiedBy_ModVersion_UsesMinimumBoundSemantics(string requirementText, string candidate, bool expected)
    {
        var requirement = VersionRequirement.Parse(requirementText);

        requirement.IsSatisfiedBy(ModVersion.Parse(candidate)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("1.22.0", "1.22.1", true)]
    [InlineData("1.22.0", "1.21.9", false)]
    [InlineData("1.22.0", "1.22.0", true)]
    public void IsSatisfiedBy_GameVersion_UsesMinimumBoundSemantics(string requirementText, string candidate, bool expected)
    {
        var requirement = VersionRequirement.Parse(requirementText);

        requirement.IsSatisfiedBy(GameVersion.Parse(candidate)).ShouldBe(expected);
    }

    [Fact]
    public void IsSatisfiedBy_PreReleaseCandidateAgainstFinalMinimum_ReturnsFalse()
    {
        // Une contrainte sur une version finale n'est pas satisfaite par une pré-release du
        // même triplet Major.Minor.Patch : rc.5 < 1.22.0 dans notre ordre.
        var requirement = VersionRequirement.Parse("1.22.0");

        requirement.IsSatisfiedBy(ModVersion.Parse("1.22.0-rc.5")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("1.22.0-rc.2", "1.22.0-rc.10", true)]
    [InlineData("1.22.0-rc.10", "1.22.0-rc.2", false)]
    public void IsSatisfiedBy_PreReleaseMinimum_ComparesSuffixNumerically(string requirementText, string candidate, bool expected)
    {
        var requirement = VersionRequirement.Parse(requirementText);

        requirement.IsSatisfiedBy(ModVersion.Parse(candidate)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(" v1.2.0 ")]
    [InlineData("V1.2.0")]
    public void Parse_DirtyMinimumBound_ParsesLeniently(string raw)
    {
        var requirement = VersionRequirement.Parse(raw);

        requirement.IsAny.ShouldBeFalse();
        requirement.IsSatisfiedBy(ModVersion.Parse("1.2.0")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("1.20.*")]
    [InlineData("abc")]
    [InlineData("1.2")]
    public void Parse_InvalidValue_ThrowsFormatException(string raw)
    {
        Should.Throw<FormatException>(() => VersionRequirement.Parse(raw));
    }

    [Theory]
    [InlineData("1.20.*")]
    [InlineData("abc")]
    [InlineData("1.2")]
    public void TryParse_InvalidValue_ReturnsFalse(string raw)
    {
        var parsed = VersionRequirement.TryParse(raw, out var requirement);

        parsed.ShouldBeFalse();
        requirement.ShouldBe(default);
    }

    [Fact]
    public void ToString_AnyVersion_ReturnsWildcard()
    {
        VersionRequirement.Parse(null).ToString().ShouldBe("*");
    }

    [Fact]
    public void ToString_MinimumBound_ReturnsCanonicalVersion()
    {
        VersionRequirement.Parse("1.2.0").ToString().ShouldBe("1.2.0");
    }

    [Fact]
    public void Equals_SameMinimum_ReturnsTrue()
    {
        var a = VersionRequirement.Parse("1.2.0");
        var b = VersionRequirement.Parse("v1.2.0");

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
    }
}