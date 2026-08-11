using Prospect.Core.Common;

using Shouldly;

namespace Prospect.Core.Tests.Common;

public class SemanticVersionCoreTests
{
    [Theory]
    [MemberData(nameof(VersionSamples.OrderedPairs), MemberType = typeof(VersionSamples))]
    public void LessThanOperator_LowerThanHigher_ReturnsTrue(string lower, string higher)
    {
        var lowerVersion = ParseVersion(lower);
        var higherVersion = ParseVersion(higher);

        (lowerVersion < higherVersion).ShouldBeTrue();
        (higherVersion < lowerVersion).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(VersionSamples.OrderedPairs), MemberType = typeof(VersionSamples))]
    public void LessThanOrEqualOperator_LowerThanHigher_ReturnsTrue(string lower, string higher)
    {
        var lowerVersion = ParseVersion(lower);
        var higherVersion = ParseVersion(higher);

        (lowerVersion <= higherVersion).ShouldBeTrue();
        (higherVersion <= lowerVersion).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(VersionSamples.OrderedPairs), MemberType = typeof(VersionSamples))]
    public void GreaterThanOperator_HigherThanLower_ReturnsTrue(string lower, string higher)
    {
        var lowerVersion = ParseVersion(lower);
        var higherVersion = ParseVersion(higher);

        (higherVersion > lowerVersion).ShouldBeTrue();
        (lowerVersion > higherVersion).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(VersionSamples.OrderedPairs), MemberType = typeof(VersionSamples))]
    public void GreaterThanOrEqualOperator_HigherThanLower_ReturnsTrue(string lower, string higher)
    {
        var lowerVersion = ParseVersion(lower);
        var higherVersion = ParseVersion(higher);

        (higherVersion >= lowerVersion).ShouldBeTrue();
        (lowerVersion >= higherVersion).ShouldBeFalse();
    }

    [Fact]
    public void LessThanOrEqualOperator_SameVersion_ReturnsTrue()
    {
        // Teste l'égalité avec <=
        var a = ParseVersion("1.22.6");
        var b = ParseVersion("1.22.6");

        (a <= b).ShouldBeTrue();
        (b <= a).ShouldBeTrue();
    }

    [Fact]
    public void GreaterThanOrEqualOperator_SameVersion_ReturnsTrue()
    {
        // Teste l'égalité avec >=
        var a = ParseVersion("1.22.0-rc.10");
        var b = ParseVersion("1.22.0-rc.10");

        (a >= b).ShouldBeTrue();
        (b >= a).ShouldBeTrue();
    }

    /// <summary>
    /// Helper pour parser SemanticVersionCore via l'interface interne TryParseStrict.
    /// </summary>
    private static SemanticVersionCore ParseVersion(string version)
    {
        if (!SemanticVersionCore.TryParseStrict(version, out var result))
        {
            throw new FormatException($"Format de version invalide : '{version}'.");
        }

        return result;
    }
}