using Prospect.Core.Common;
using Prospect.Core.ModDb;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

public sealed class ModInfoParserTests
{
    [Fact]
    public void Parse_ConfigLibSample_ReadsEveryDeclaredField()
    {
        var info = ModInfoParser.Parse(ModInfoSamples.ConfigLib).Info.ShouldNotBeNull();

        info.ModId.ShouldBe("configlib");
        info.Name.ShouldBe("Config lib");
        info.Version.ShouldBe(ModVersion.Parse("1.12.0"));
        info.Type.ShouldBe(ModType.Code);
        info.Side.ShouldBe(ModSide.Universal);
        info.Authors.ShouldBe(["Maltiez", "The Insanity God"]);
        info.RequiredOnClient.ShouldBeTrue();
        info.RequiredOnServer.ShouldBeFalse();
        info.Dependencies["vsimgui"].IsSatisfiedBy(ModVersion.Parse("1.2.0")).ShouldBeTrue();
        info.Dependencies["vsimgui"].IsSatisfiedBy(ModVersion.Parse("1.1.9")).ShouldBeFalse();
    }

    [Fact]
    public void Parse_JsonPatchesLibSample_AcceptsAnEmptyDependencyObject()
    {
        var info = ModInfoParser.Parse(ModInfoSamples.JsonPatchesLib).Info.ShouldNotBeNull();

        info.Dependencies.ShouldBeEmpty();
        info.ModDependencies.ShouldBeEmpty();
        info.GameRequirement.ShouldBeNull();
        info.Contributors.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_ExtraInfoSample_SplitsTheSpecialGameDependencyFromRealModDependencies()
    {
        var info = ModInfoParser.Parse(ModInfoSamples.ExtraInfo).Info.ShouldNotBeNull();

        info.Side.ShouldBe(ModSide.Client);
        info.Contributors.ShouldBe(["Novocain"]);
        info.GameRequirement.ShouldNotBeNull();
        info.GameRequirement!.Value.IsSatisfiedBy(GameVersion.Parse("1.22.1")).ShouldBeTrue();
        info.GameRequirement!.Value.IsSatisfiedBy(GameVersion.Parse("1.21.3")).ShouldBeFalse();
        info.ModDependencies.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_PascalCaseKeys_AreResolvedCaseInsensitively()
    {
        var info = ModInfoParser.Parse(ModInfoSamples.PascalCaseKeys).Info.ShouldNotBeNull();

        info.ModId.ShouldBe("carrycapacity");
        info.Name.ShouldBe("Carry Capacity");
        info.Version.ShouldBe(ModVersion.Parse("1.8.0"));
        info.Type.ShouldBe(ModType.Content);
        info.Side.ShouldBe(ModSide.Universal);
        info.Authors.ShouldBe(["copygirl"]);
    }

    [Fact]
    public void Parse_CommentsAndTrailingCommas_AreTolerated()
    {
        var info = ModInfoParser.Parse(ModInfoSamples.WithCommentsAndTrailingComma).Info.ShouldNotBeNull();

        info.ModId.ShouldBe("commentedmod");
        info.Version.ShouldBe(ModVersion.Parse("0.3.1"));
        info.Dependencies.ShouldContainKey("configlib");
    }

    [Fact]
    public void Parse_MissingModId_DerivesItFromTheNameLikeTheGameDoes()
    {
        var info = ModInfoParser.Parse(ModInfoSamples.WithoutModId).Info.ShouldNotBeNull();

        info.ModId.ShouldBe("mygreatmod");
        info.Name.ShouldBe("My Great Mod!");
    }

    [Fact]
    public void Parse_MissingSide_DefaultsToUniversalAndRequiredFlagsDefaultToTrue()
    {
        var info = ModInfoParser.Parse(ModInfoSamples.WithoutModId).Info.ShouldNotBeNull();

        info.Side.ShouldBe(ModSide.Universal);
        info.RequiredOnClient.ShouldBeTrue();
        info.RequiredOnServer.ShouldBeTrue();
        info.Type.ShouldBe(ModType.Unknown);
    }

    [Theory]
    [InlineData("\"Client\"", ModSide.Client)]
    [InlineData("\"client\"", ModSide.Client)]
    [InlineData("\"SERVER\"", ModSide.Server)]
    [InlineData("\"Universal\"", ModSide.Universal)]
    [InlineData("\"n'importe quoi\"", ModSide.Universal)]
    [InlineData("42", ModSide.Universal)]
    public void Parse_SideValue_IsCaseInsensitiveAndDefaultsToUniversal(string rawSide, ModSide expected)
    {
        var json = $$"""{ "modid": "x", "name": "X", "side": {{rawSide}} }""";

        ModInfoParser.Parse(json).Info!.Side.ShouldBe(expected);
    }

    [Fact]
    public void Parse_WildcardAndEmptyDependencies_MeanAnyVersion()
    {
        var info = ModInfoParser.Parse(ModInfoSamples.WithLooseDependencies).Info.ShouldNotBeNull();

        info.Dependencies["anyversion"].IsAny.ShouldBeTrue();
        info.Dependencies["star"].IsAny.ShouldBeTrue();
    }

    [Fact]
    public void Parse_UnreadableDependencyConstraint_IsReportedRatherThanTreatedAsAnyVersion()
    {
        // « 1.20.* » n'existe dans aucune grammaire du jeu ni du ModDB : la traiter comme un joker
        // reviendrait à affirmer une compatibilité que personne n'a vérifiée.
        var info = ModInfoParser.Parse(ModInfoSamples.WithLooseDependencies).Info.ShouldNotBeNull();

        info.Dependencies.ShouldNotContainKey("broken");
        info.UnreadableDependencies.ShouldContain("broken: 1.20.*");
    }

    [Fact]
    public void Parse_SurvivalAndCreative_AreExcludedFromModDependenciesLikeTheModDbDoes()
    {
        var info = ModInfoParser.Parse(ModInfoSamples.WithLooseDependencies).Info.ShouldNotBeNull();

        info.Dependencies.ShouldContainKey("survival");
        info.ModDependencies.ShouldNotContainKey("survival");
        info.ModDependencies.ShouldNotContainKey("creative");
        info.ModDependencies.Keys.ShouldBe(["anyversion", "star"], ignoreOrder: true);
    }

    [Fact]
    public void Parse_AuthorsGivenAsASingleString_AreStillRead()
    {
        var info = ModInfoParser.Parse("""{ "modid": "x", "name": "X", "authors": "Solo" }""").Info.ShouldNotBeNull();

        info.Authors.ShouldBe(["Solo"]);
    }

    [Fact]
    public void Parse_UnparseableVersion_KeepsTheRawValueWithoutFailing()
    {
        var info = ModInfoParser.Parse("""{ "modid": "x", "name": "X", "version": "v2 beta" }""").Info.ShouldNotBeNull();

        info.Version.ShouldBeNull();
        info.RawVersion.ShouldBe("v2 beta");
    }

    [Fact]
    public void Parse_MalformedJson_ReportsTheProblemInsteadOfThrowing()
    {
        var result = ModInfoParser.Parse(ModInfoSamples.Malformed);

        result.IsIdentified.ShouldBeFalse();
        result.Problem.ShouldBe(ModInfoProblem.MalformedJson);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyContent_IsMalformed(string? json)
        => ModInfoParser.Parse(json).Problem.ShouldBe(ModInfoProblem.MalformedJson);

    [Fact]
    public void Parse_JsonArrayInsteadOfObject_IsMalformed()
        => ModInfoParser.Parse("[1, 2, 3]").Problem.ShouldBe(ModInfoProblem.MalformedJson);

    [Fact]
    public void Parse_ValidJsonWithoutAnyIdentity_ReportsMissingIdentity()
    {
        var result = ModInfoParser.Parse("""{ "version": "1.0.0" }""");

        result.IsIdentified.ShouldBeFalse();
        result.Problem.ShouldBe(ModInfoProblem.MissingIdentity);
    }

    [Fact]
    public void Parse_NameWithNoUsableCharacters_ReportsMissingIdentity()
        => ModInfoParser.Parse("""{ "name": "!!! ???" }""").Problem.ShouldBe(ModInfoProblem.MissingIdentity);

    [Fact]
    public void Parse_QuotedBooleanFlags_AreAccepted()
    {
        var info = ModInfoParser.Parse("""{ "modid": "x", "name": "X", "requiredOnServer": "false" }""").Info.ShouldNotBeNull();

        info.RequiredOnServer.ShouldBeFalse();
    }

    [Theory]
    [InlineData("My Great Mod!", "mygreatmod")]
    [InlineData("  Carry Capacity  ", "carrycapacity")]
    [InlineData("Mod 2000", "mod2000")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void DeriveModId_FollowsTheGameRule(string? name, string expected)
        => ModInfoParser.DeriveModId(name).ShouldBe(expected);

    [Theory]
    [InlineData("game", true)]
    [InlineData("survival", true)]
    [InlineData("creative", true)]
    [InlineData("Game", true)]
    [InlineData("configlib", false)]
    public void IsSpecialDependencyId_MatchesTheModDbIgnoreList(string identifier, bool expected)
        => ModInfoParser.IsSpecialDependencyId(identifier).ShouldBe(expected);
}