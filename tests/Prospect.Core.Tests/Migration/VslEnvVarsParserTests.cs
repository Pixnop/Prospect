using Prospect.Core.Migration;

using Shouldly;

namespace Prospect.Core.Tests.Migration;

public class VslEnvVarsParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_BlankInput_ReturnsEmptyDictionary(string? input)
    {
        VslEnvVarsParser.Parse(input).ShouldBeEmpty();
    }

    [Fact]
    public void Parse_SinglePair_ReturnsOneEntry()
    {
        var result = VslEnvVarsParser.Parse("MESA_GLTHREAD=true");

        result.ShouldContainKeyAndValue("MESA_GLTHREAD", "true");
    }

    [Fact]
    public void Parse_MultiplePairsSeparatedByComma_ReturnsAllEntries()
    {
        var result = VslEnvVarsParser.Parse("MESA_GLTHREAD=true,DXVK_HUD=fps");

        result.Count.ShouldBe(2);
        result.ShouldContainKeyAndValue("MESA_GLTHREAD", "true");
        result.ShouldContainKeyAndValue("DXVK_HUD", "fps");
    }

    [Fact]
    public void Parse_SpacesAroundKeyAndValue_AreTrimmed()
    {
        var result = VslEnvVarsParser.Parse(" MESA_GLTHREAD = true , DXVK_HUD = fps ");

        result.ShouldContainKeyAndValue("MESA_GLTHREAD", "true");
        result.ShouldContainKeyAndValue("DXVK_HUD", "fps");
    }

    [Fact]
    public void Parse_ValueContainingEqualsSign_IsKeptWhole()
    {
        // Amélioration délibérée par rapport à VS Launcher (voir la docstring de la classe) : son
        // découpage sur TOUS les "=" perdrait le "=b" final ici.
        var result = VslEnvVarsParser.Parse("SOME_KEY=a=b");

        result.ShouldContainKeyAndValue("SOME_KEY", "a=b");
    }

    [Fact]
    public void Parse_EntryWithoutEqualsSign_IsIgnored()
    {
        var result = VslEnvVarsParser.Parse("NOT_A_PAIR,MESA_GLTHREAD=true");

        result.Count.ShouldBe(1);
        result.ShouldContainKeyAndValue("MESA_GLTHREAD", "true");
    }

    [Fact]
    public void Parse_EntryWithEmptyKey_IsIgnored()
    {
        var result = VslEnvVarsParser.Parse("=novalue,MESA_GLTHREAD=true");

        result.Count.ShouldBe(1);
        result.ShouldContainKeyAndValue("MESA_GLTHREAD", "true");
    }

    [Fact]
    public void Parse_EmptySegmentsBetweenCommas_AreIgnored()
    {
        var result = VslEnvVarsParser.Parse("MESA_GLTHREAD=true,,DXVK_HUD=fps");

        result.Count.ShouldBe(2);
    }

    [Fact]
    public void Parse_ValueMayBeEmpty_IsKeptAsEmptyString()
    {
        var result = VslEnvVarsParser.Parse("SOME_KEY=");

        result.ShouldContainKeyAndValue("SOME_KEY", string.Empty);
    }
}