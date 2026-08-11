using Prospect.Core.Instances;

using Shouldly;

namespace Prospect.Core.Tests.Instances;

public class InstanceSchemaVersionUnsupportedExceptionTests
{
    [Fact]
    public void Constructor_NewerSchema_ExposesVersionsAndMentionsNewerBuild()
    {
        var exception = new InstanceSchemaVersionUnsupportedException("/data/prospect/instances/homestead/instance.json", 5, 1);

        exception.Path.ShouldBe("/data/prospect/instances/homestead/instance.json");
        exception.FoundSchemaVersion.ShouldBe(5);
        exception.CurrentSchemaVersion.ShouldBe(1);
        exception.Message.ShouldContain("5");
        exception.Message.ShouldContain("1");
    }

    [Fact]
    public void Constructor_OlderSchemaWithoutMigrationPath_ExposesVersions()
    {
        var exception = new InstanceSchemaVersionUnsupportedException("/data/instance.json", 0, 1);

        exception.FoundSchemaVersion.ShouldBe(0);
        exception.CurrentSchemaVersion.ShouldBe(1);
    }

    [Fact]
    public void Constructor_Parameterless_ExposesEmptyPath()
    {
        var exception = new InstanceSchemaVersionUnsupportedException();

        exception.Path.ShouldBe(string.Empty);
    }

    [Fact]
    public void Constructor_MessageOnly_ExposesEmptyPath()
    {
        var exception = new InstanceSchemaVersionUnsupportedException("un message");

        exception.Path.ShouldBe(string.Empty);
        exception.Message.ShouldBe("un message");
    }
}