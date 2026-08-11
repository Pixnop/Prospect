using Prospect.Desktop.Formatting;

using Shouldly;

namespace Prospect.Desktop.Tests.Formatting;

public class InstanceIconKeyResolverTests
{
    [Theory]
    [InlineData("builtin:layers", "layers")]
    [InlineData("builtin:package", "package")]
    [InlineData("builtin:default", "layers")]
    [InlineData("builtin:", "layers")]
    [InlineData("file:custom.png", "layers")]
    [InlineData("", "layers")]
    public void Resolve_ReturnsFallbackForAnythingButAKnownBuiltinIcon(string icon, string expected)
        => InstanceIconKeyResolver.Resolve(icon).ShouldBe(expected);

    [Fact]
    public void Resolve_Null_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => InstanceIconKeyResolver.Resolve(null!));
}