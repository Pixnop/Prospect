using Prospect.Core.Instances;

using Shouldly;

namespace Prospect.Core.Tests.Instances;

public class InstanceNameInvalidExceptionTests
{
    [Fact]
    public void Constructor_AttemptedName_ExposesAttemptedName()
    {
        var exception = new InstanceNameInvalidException("   ");

        exception.AttemptedName.ShouldBe("   ");
        exception.Message.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Constructor_Parameterless_ExposesEmptyAttemptedName()
    {
        var exception = new InstanceNameInvalidException();

        exception.AttemptedName.ShouldBe(string.Empty);
    }

    [Fact]
    public void Constructor_AttemptedNameAndInnerException_ExposesBoth()
    {
        var inner = new InvalidOperationException("cause");

        var exception = new InstanceNameInvalidException(string.Empty, inner);

        exception.InnerException.ShouldBeSameAs(inner);
    }
}