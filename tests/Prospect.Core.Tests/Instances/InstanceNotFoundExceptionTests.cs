using Prospect.Core.Instances;

using Shouldly;

namespace Prospect.Core.Tests.Instances;

public class InstanceNotFoundExceptionTests
{
    [Fact]
    public void Constructor_Slug_ExposesSlugAndMessage()
    {
        var exception = new InstanceNotFoundException("homestead");

        exception.Slug.ShouldBe("homestead");
        exception.Message.ShouldContain("homestead");
    }

    [Fact]
    public void Constructor_Parameterless_ExposesEmptySlug()
    {
        var exception = new InstanceNotFoundException();

        exception.Slug.ShouldBe(string.Empty);
    }

    [Fact]
    public void Constructor_SlugAndInnerException_ExposesBoth()
    {
        var inner = new InvalidOperationException("cause");

        var exception = new InstanceNotFoundException("homestead", inner);

        exception.Slug.ShouldBe("homestead");
        exception.InnerException.ShouldBeSameAs(inner);
    }
}