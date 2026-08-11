using Prospect.Desktop.Formatting;

using Shouldly;

namespace Prospect.Desktop.Tests.Formatting;

/// <summary>
/// Tests unitaires purs (aucun <c>[AvaloniaFact]</c> requis : <see cref="RelativeDateFormatter"/>
/// ne touche à rien d'Avalonia) du formateur de dates relatives françaises.
/// </summary>
public class RelativeDateFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Format_Null_ReturnsJamais()
    {
        RelativeDateFormatter.Format(null, Now).ShouldBe("jamais");
    }

    [Fact]
    public void Format_SameUtcDayEarlierInDay_ReturnsAujourdhui()
    {
        var value = Now.AddHours(-2);

        RelativeDateFormatter.Format(value, Now).ShouldBe("aujourd'hui");
    }

    [Fact]
    public void Format_PreviousUtcDay_ReturnsHier()
    {
        var value = Now.AddDays(-1);

        RelativeDateFormatter.Format(value, Now).ShouldBe("hier");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(30)]
    public void Format_UpToThirtyDaysAgo_ReturnsIlYANJours(int days)
    {
        var value = Now.AddDays(-days);

        RelativeDateFormatter.Format(value, Now).ShouldBe($"il y a {days} jours");
    }

    [Fact]
    public void Format_MoreThanThirtyDaysAgo_FallsBackToAbsoluteDate()
    {
        var value = Now.AddDays(-31);

        var result = RelativeDateFormatter.Format(value, Now);

        result.ShouldNotContain("il y a");
        result.ShouldNotBe("jamais");
    }

    [Fact]
    public void Format_FutureSameUtcDay_IsToleratedAsAujourdhui()
    {
        var value = Now.AddMinutes(5);

        RelativeDateFormatter.Format(value, Now).ShouldBe("aujourd'hui");
    }

    [Fact]
    public void Format_FutureBeyondToday_IsToleratedAsAbsoluteDateRatherThanThrowing()
    {
        var value = Now.AddDays(3);

        var result = RelativeDateFormatter.Format(value, Now);

        result.ShouldNotBeNullOrEmpty();
        result.ShouldNotContain("il y a");
    }
}