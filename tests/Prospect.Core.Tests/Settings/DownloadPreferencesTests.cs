using Prospect.Core.Settings;

using Shouldly;

namespace Prospect.Core.Tests.Settings;

public class DownloadPreferencesTests
{
    [Fact]
    public void Default_MatchesDownloadOptionsDefault()
    {
        // Aligné sur Prospect.Core.Http.DownloadOptions.Default (2) : un utilisateur qui n'a jamais
        // touché ce réglage doit obtenir exactement le comportement historique du DownloadManager.
        DownloadPreferences.Default.MaxParallelDownloads.ShouldBe(2);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(8, 8)]
    public void Clamped_ValueWithinRange_IsUnchanged(int value, int expected)
    {
        var preferences = new DownloadPreferences { MaxParallelDownloads = value };

        preferences.Clamped().MaxParallelDownloads.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void Clamped_ValueBelowFloor_IsRaisedToMinimum(int value)
    {
        var preferences = new DownloadPreferences { MaxParallelDownloads = value };

        preferences.Clamped().MaxParallelDownloads.ShouldBe(DownloadPreferences.MinParallelDownloads);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(64)]
    [InlineData(int.MaxValue)]
    public void Clamped_ValueAboveCeiling_IsLoweredToMaximum(int value)
    {
        var preferences = new DownloadPreferences { MaxParallelDownloads = value };

        preferences.Clamped().MaxParallelDownloads.ShouldBe(DownloadPreferences.MaxParallelDownloadsCeiling);
    }

    [Fact]
    public void AllowedChoices_AreAllWithinBounds()
    {
        foreach (var choice in DownloadPreferences.AllowedChoices)
        {
            choice.ShouldBeInRange(DownloadPreferences.MinParallelDownloads, DownloadPreferences.MaxParallelDownloadsCeiling);
        }
    }
}