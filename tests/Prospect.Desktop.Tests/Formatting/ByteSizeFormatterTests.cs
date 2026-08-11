using Prospect.Desktop.Formatting;

using Shouldly;

namespace Prospect.Desktop.Tests.Formatting;

public class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-1, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1_500, "1.5 KB")]
    [InlineData(619_177_967, "619.2 MB")]
    [InlineData(6_000_000_000, "6.00 GB")]
    public void Format_UsesTheSameUnitsAsTheOfficialCatalog(long bytes, string expected)
        => ByteSizeFormatter.Format(bytes).ShouldBe(expected);

    [Fact]
    public void FormatSpeed_WithoutAnyMeasuredRate_IsEmpty()
        => ByteSizeFormatter.FormatSpeed(0d).ShouldBeEmpty();

    [Fact]
    public void FormatSpeed_MeasuredRate_ReadsPerSecond()
        => ByteSizeFormatter.FormatSpeed(4_200_000d).ShouldBe("4.2 MB/s");

    [Fact]
    public void FormatProgress_KnownTotal_ShowsBothSides()
        => ByteSizeFormatter.FormatProgress(230_100_000, 590_500_000).ShouldBe("230.1 MB / 590.5 MB");

    [Fact]
    public void FormatProgress_UnknownTotal_ShowsOnlyWhatArrived()
        => ByteSizeFormatter.FormatProgress(230_100_000, null).ShouldBe("230.1 MB");
}