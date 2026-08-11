using Prospect.Core.Common;
using Prospect.Desktop.Formatting;

using Shouldly;

namespace Prospect.Desktop.Tests.Formatting;

public class ChannelBadgePresentationTests
{
    [Theory]
    [InlineData(GameVersionChannel.Stable, "stable")]
    [InlineData(GameVersionChannel.Pre, "pre")]
    [InlineData(GameVersionChannel.Rc, "unstable")]
    [InlineData(GameVersionChannel.Dev, "unstable")]
    public void ToBadgeTone_MapsEachChannelToExpectedTone(GameVersionChannel channel, string expectedTone)
    {
        ChannelBadgePresentation.ToBadgeTone(channel).ShouldBe(expectedTone);
    }
}