using Prospect.Core.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Storage;

public class CorruptedFileExceptionTests
{
    [Fact]
    public void Constructor_PathAndInnerException_ExposesPathAndInnerException()
    {
        var inner = new FormatException("JSON invalide");

        var exception = new CorruptedFileException("/data/prospect/prospect.json", inner);

        exception.Path.ShouldBe("/data/prospect/prospect.json");
        exception.InnerException.ShouldBeSameAs(inner);
        exception.Message.ShouldContain("/data/prospect/prospect.json");
    }

    [Fact]
    public void Constructor_Parameterless_ExposesEmptyPath()
    {
        var exception = new CorruptedFileException();

        exception.Path.ShouldBe(string.Empty);
    }

    [Fact]
    public void Constructor_MessageOnly_ExposesEmptyPath()
    {
        var exception = new CorruptedFileException("un message");

        exception.Path.ShouldBe(string.Empty);
        exception.Message.ShouldBe("un message");
    }
}