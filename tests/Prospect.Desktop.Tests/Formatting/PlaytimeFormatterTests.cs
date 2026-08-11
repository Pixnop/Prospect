using Prospect.Desktop.Formatting;

using Shouldly;

namespace Prospect.Desktop.Tests.Formatting;

public class PlaytimeFormatterTests
{
    [Theory]
    [InlineData(0, "jamais joué")]
    [InlineData(-1, "jamais joué")]
    [InlineData(1, "joué < 1 h")]
    [InlineData(3599, "joué < 1 h")]
    [InlineData(3600, "joué 1 h")]
    [InlineData(460_800, "joué 128 h")]
    public void Format_MatchesTheDesignFragment(long totalSeconds, string expected)
        => PlaytimeFormatter.Format(totalSeconds).ShouldBe(expected);
}