using Prospect.Core.Runtime;

using Shouldly;

namespace Prospect.Core.Tests.Runtime;

public sealed class DotnetRuntimeListParserTests
{
    [Fact]
    public void Parse_TypicalOutput_ReturnsEveryRuntimeWithNameAndVersion()
    {
        const string output = """
            Microsoft.AspNetCore.App 8.0.10 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]
            Microsoft.NETCore.App 8.0.10 [/usr/share/dotnet/shared/Microsoft.NETCore.App]
            Microsoft.NETCore.App 10.0.0 [/usr/share/dotnet/shared/Microsoft.NETCore.App]
            """;

        var runtimes = DotnetRuntimeListParser.Parse(output);

        runtimes.ShouldBe(
        [
            new DotnetRuntimeInfo("Microsoft.AspNetCore.App", new Version(8, 0, 10)),
            new DotnetRuntimeInfo("Microsoft.NETCore.App", new Version(8, 0, 10)),
            new DotnetRuntimeInfo("Microsoft.NETCore.App", new Version(10, 0, 0)),
        ]);
    }

    [Fact]
    public void Parse_WindowsStylePathWithDriveLetterAndBackslashes_StillReadsNameAndVersion()
    {
        const string output = "Microsoft.NETCore.App 8.0.10 [C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App]";

        var runtimes = DotnetRuntimeListParser.Parse(output);

        runtimes.ShouldBe([new DotnetRuntimeInfo("Microsoft.NETCore.App", new Version(8, 0, 10))]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_EmptyOrBlankOutput_ReturnsEmptyList(string? output)
    {
        DotnetRuntimeListParser.Parse(output!).ShouldBeEmpty();
    }

    [Fact]
    public void Parse_NoRuntimesInstalledMessage_ReturnsEmptyListRatherThanThrowing()
    {
        const string output = "No runtimes were found.";

        DotnetRuntimeListParser.Parse(output).ShouldBeEmpty();
    }

    [Fact]
    public void Parse_MalformedLineWithoutVersion_IsSkippedButOtherLinesStillParse()
    {
        const string output = """
            this is not a valid line at all
            Microsoft.NETCore.App 8.0.10 [/usr/share/dotnet/shared/Microsoft.NETCore.App]
            """;

        var runtimes = DotnetRuntimeListParser.Parse(output);

        runtimes.ShouldBe([new DotnetRuntimeInfo("Microsoft.NETCore.App", new Version(8, 0, 10))]);
    }

    [Fact]
    public void Parse_BlankLinesBetweenEntries_AreIgnored()
    {
        const string output = "\nMicrosoft.NETCore.App 8.0.10 [/path]\n\n\nMicrosoft.NETCore.App 10.0.0 [/path]\n";

        var runtimes = DotnetRuntimeListParser.Parse(output);

        runtimes.Count.ShouldBe(2);
    }

    [Fact]
    public void Parse_CarriageReturnLineEndings_AreTrimmed()
    {
        const string output = "Microsoft.NETCore.App 8.0.10 [/path]\r\nMicrosoft.NETCore.App 10.0.0 [/path]\r\n";

        var runtimes = DotnetRuntimeListParser.Parse(output);

        runtimes.ShouldBe(
        [
            new DotnetRuntimeInfo("Microsoft.NETCore.App", new Version(8, 0, 10)),
            new DotnetRuntimeInfo("Microsoft.NETCore.App", new Version(10, 0, 0)),
        ]);
    }
}