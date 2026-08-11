using Prospect.Core.Common;

using Shouldly;

namespace Prospect.Core.Tests.Common;

public class GameVersionTests
{
    [Theory]
    [MemberData(nameof(VersionSamples.Valid), MemberType = typeof(VersionSamples))]
    public void Parse_ValidStrictString_ReturnsExpectedComponents(
        string input, int major, int minor, int patch, string? suffix, int suffixNumber)
    {
        var version = GameVersion.Parse(input);

        version.Major.ShouldBe(major);
        version.Minor.ShouldBe(minor);
        version.Patch.ShouldBe(patch);
        version.ToString().ShouldBe(suffix is null ? $"{major}.{minor}.{patch}" : $"{major}.{minor}.{patch}-{suffix}.{suffixNumber}");
    }

    [Theory]
    [MemberData(nameof(VersionSamples.Valid), MemberType = typeof(VersionSamples))]
    public void TryParse_ValidStrictString_ReturnsTrueWithSameResultAsParse(
        string input, int major, int minor, int patch, string? suffix, int suffixNumber)
    {
        _ = major;
        _ = minor;
        _ = patch;
        _ = suffix;
        _ = suffixNumber;

        var parsed = GameVersion.TryParse(input, out var version);

        parsed.ShouldBeTrue();
        version.ShouldBe(GameVersion.Parse(input));
    }

    [Theory]
    [MemberData(nameof(VersionSamples.Invalid), MemberType = typeof(VersionSamples))]
    public void Parse_InvalidString_ThrowsFormatException(string input)
    {
        Should.Throw<FormatException>(() => GameVersion.Parse(input));
    }

    [Theory]
    [MemberData(nameof(VersionSamples.Invalid), MemberType = typeof(VersionSamples))]
    public void TryParse_InvalidString_ReturnsFalse(string input)
    {
        var parsed = GameVersion.TryParse(input, out var version);

        parsed.ShouldBeFalse();
        version.ShouldBe(default);
    }

    [Fact]
    public void Parse_Null_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => GameVersion.Parse(null!));
    }

    [Fact]
    public void TryParse_Null_ReturnsFalse()
    {
        var parsed = GameVersion.TryParse(null, out var version);

        parsed.ShouldBeFalse();
        version.ShouldBe(default);
    }

    [Theory]
    [MemberData(nameof(VersionSamples.LenientOnly), MemberType = typeof(VersionSamples))]
    public void TryParse_DirtyButTolerableString_ReturnsCanonicalForm(string input, string canonical)
    {
        var parsed = GameVersion.TryParse(input, out var version);

        parsed.ShouldBeTrue();
        version.ToString().ShouldBe(canonical);
    }

    [Theory]
    [MemberData(nameof(VersionSamples.LenientOnly), MemberType = typeof(VersionSamples))]
    public void Parse_DirtyButTolerableString_ThrowsFormatException(string input, string canonical)
    {
        _ = canonical;

        Should.Throw<FormatException>(() => GameVersion.Parse(input));
    }

    [Theory]
    [MemberData(nameof(VersionSamples.OrderedPairs), MemberType = typeof(VersionSamples))]
    public void CompareTo_LowerThanHigher_ReturnsNegative(string lower, string higher)
    {
        var lowerVersion = GameVersion.Parse(lower);
        var higherVersion = GameVersion.Parse(higher);

        lowerVersion.CompareTo(higherVersion).ShouldBeLessThan(0);
        higherVersion.CompareTo(lowerVersion).ShouldBeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(VersionSamples.OrderedPairs), MemberType = typeof(VersionSamples))]
    public void ComparisonOperators_LowerThanHigher_AgreeWithCompareTo(string lower, string higher)
    {
        var lowerVersion = GameVersion.Parse(lower);
        var higherVersion = GameVersion.Parse(higher);

        (lowerVersion < higherVersion).ShouldBeTrue();
        (lowerVersion <= higherVersion).ShouldBeTrue();
        (higherVersion > lowerVersion).ShouldBeTrue();
        (higherVersion >= lowerVersion).ShouldBeTrue();
        (lowerVersion > higherVersion).ShouldBeFalse();
        (higherVersion < lowerVersion).ShouldBeFalse();
    }

    [Fact]
    public void CompareTo_SameVersion_ReturnsZero()
    {
        var a = GameVersion.Parse("1.22.6");
        var b = GameVersion.Parse("1.22.6");

        a.CompareTo(b).ShouldBe(0);
    }

    [Fact]
    public void Equals_SameComponents_ReturnsTrue()
    {
        var a = GameVersion.Parse("1.22.0-rc.10");
        var b = GameVersion.Parse("1.22.0-rc.10");

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        (a != b).ShouldBeFalse();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentSuffixNumber_ReturnsFalse()
    {
        var a = GameVersion.Parse("1.22.0-rc.2");
        var b = GameVersion.Parse("1.22.0-rc.10");

        a.Equals(b).ShouldBeFalse();
        (a == b).ShouldBeFalse();
        (a != b).ShouldBeTrue();
    }

    [Theory]
    [InlineData("1.22.6", GameVersionChannel.Stable)]
    [InlineData("1.9.14", GameVersionChannel.Stable)]
    [InlineData("1.22.0-rc.10", GameVersionChannel.Rc)]
    [InlineData("1.22.0-pre.5", GameVersionChannel.Pre)]
    [InlineData("1.22.0-dev.1", GameVersionChannel.Dev)]
    public void Channel_DerivedFromSuffix_MatchesExpectedChannel(string input, GameVersionChannel expected)
    {
        var version = GameVersion.Parse(input);

        version.Channel.ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(VersionSamples.Valid), MemberType = typeof(VersionSamples))]
    public void ToString_RoundTripsThroughParse(string input, int major, int minor, int patch, string? suffix, int suffixNumber)
    {
        _ = major;
        _ = minor;
        _ = patch;
        _ = suffix;
        _ = suffixNumber;

        var version = GameVersion.Parse(input);
        var roundTripped = GameVersion.Parse(version.ToString());

        roundTripped.ShouldBe(version);
    }
}