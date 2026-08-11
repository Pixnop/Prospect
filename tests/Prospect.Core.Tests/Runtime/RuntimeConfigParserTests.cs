using Prospect.Core.Runtime;

using Shouldly;

namespace Prospect.Core.Tests.Runtime;

public sealed class RuntimeConfigParserTests
{
    [Fact]
    public void Parse_StandardShape_ReadsFrameworkNameAndVersion()
    {
        const string json = """
            {
              "runtimeOptions": {
                "tfm": "net8.0",
                "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.10" },
                "configProperties": { "System.Reflection.Metadata.MetadataUpdater.IsSupported": false }
              }
            }
            """;

        var requirement = RuntimeConfigParser.Parse(json);

        requirement.IsKnown.ShouldBeTrue();
        requirement.FrameworkName.ShouldBe("Microsoft.NETCore.App");
        requirement.Version.ShouldBe(new Version(8, 0, 10));
    }

    [Fact]
    public void Parse_FrameworksArrayInsteadOfSingularFramework_ReadsFirstEntry()
    {
        const string json = """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "frameworks": [
                  { "name": "Microsoft.NETCore.App", "version": "10.0.0" },
                  { "name": "Microsoft.AspNetCore.App", "version": "10.0.0" }
                ]
              }
            }
            """;

        var requirement = RuntimeConfigParser.Parse(json);

        requirement.IsKnown.ShouldBeTrue();
        requirement.FrameworkName.ShouldBe("Microsoft.NETCore.App");
        requirement.Version.ShouldBe(new Version(10, 0, 0));
    }

    [Fact]
    public void Parse_MissingRuntimeOptions_ReturnsUnknown()
    {
        RuntimeConfigParser.Parse("{}").ShouldBe(GameRuntimeRequirement.Unknown);
    }

    [Fact]
    public void Parse_MissingFrameworkBlock_ReturnsUnknown()
    {
        const string json = """{ "runtimeOptions": { "tfm": "net8.0" } }""";

        RuntimeConfigParser.Parse(json).ShouldBe(GameRuntimeRequirement.Unknown);
    }

    [Fact]
    public void Parse_VersionNotParseable_ReturnsUnknown()
    {
        const string json = """
            { "runtimeOptions": { "framework": { "name": "Microsoft.NETCore.App", "version": "not-a-version" } } }
            """;

        RuntimeConfigParser.Parse(json).ShouldBe(GameRuntimeRequirement.Unknown);
    }

    [Fact]
    public void Parse_NameMissing_ReturnsUnknown()
    {
        const string json = """{ "runtimeOptions": { "framework": { "version": "8.0.10" } } }""";

        RuntimeConfigParser.Parse(json).ShouldBe(GameRuntimeRequirement.Unknown);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{ "runtimeOptions": "oops" }""")]
    [InlineData("""{ "runtimeOptions": { "framework": "oops" } }""")]
    [InlineData("""{ "runtimeOptions": { "framework": [1, 2, 3] } }""")]
    [InlineData("""{ "runtimeOptions": { "frameworks": "oops" } }""")]
    public void Parse_InvalidOrUnexpectedJson_ReturnsUnknownRatherThanThrowing(string json)
    {
        RuntimeConfigParser.Parse(json).ShouldBe(GameRuntimeRequirement.Unknown);
    }

    [Fact]
    public void GameRuntimeRequirement_ToString_KnownRequirement_ShowsFrameworkAndVersion()
    {
        GameRuntimeRequirement.Known("Microsoft.NETCore.App", new Version(8, 0, 10)).ToString()
            .ShouldBe("Microsoft.NETCore.App 8.0.10");
    }

    [Fact]
    public void GameRuntimeRequirement_ToString_Unknown_ShowsPlaceholder()
    {
        GameRuntimeRequirement.Unknown.ToString().ShouldBe("runtime inconnu");
    }
}