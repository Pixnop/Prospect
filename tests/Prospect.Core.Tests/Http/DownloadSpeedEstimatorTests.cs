using Prospect.Core.Http;
using Prospect.Core.Tests.Common;

using Shouldly;

namespace Prospect.Core.Tests.Http;

public sealed class DownloadSpeedEstimatorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Update_FirstMeasurement_IsTheRawRate()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);
        estimator.Start(0);

        clock.UtcNow = Noon.AddSeconds(2);

        estimator.Update(2_000_000).ShouldBe(1_000_000d);
    }

    [Fact]
    public void Update_SuddenSlowdown_IsSmoothedRatherThanFollowedExactly()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock, smoothing: 0.5d);
        estimator.Start(0);

        clock.UtcNow = Noon.AddSeconds(1);
        estimator.Update(1_000_000);
        clock.UtcNow = Noon.AddSeconds(2);
        var smoothed = estimator.Update(1_000_000);

        smoothed.ShouldBe(500_000d);
    }

    [Fact]
    public void Update_TwoMeasurementsAtTheSameInstant_KeepsThePreviousSpeed()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);
        estimator.Start(0);
        clock.UtcNow = Noon.AddSeconds(1);
        var first = estimator.Update(500);

        estimator.Update(900).ShouldBe(first);
    }

    [Fact]
    public void Update_WithoutStart_StartsOnTheFirstCall()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);

        estimator.Update(1000).ShouldBe(0d);

        clock.UtcNow = Noon.AddSeconds(1);
        estimator.Update(3000).ShouldBe(2000d);
    }

    [Fact]
    public void Constructor_NullClock_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => new DownloadSpeedEstimator(null!));

    [Fact]
    public void Ratio_WithoutAKnownTotal_IsNull()
        => new DownloadProgress(500, null, 0d).Ratio.ShouldBeNull();

    [Fact]
    public void Ratio_WithAKnownTotal_IsTheFraction()
        => new DownloadProgress(500, 1000, 0d).Ratio.ShouldBe(0.5d);

    [Fact]
    public void Ratio_MoreBytesThanAnnounced_IsClampedToOne()
        => new DownloadProgress(1500, 1000, 0d).Ratio.ShouldBe(1d);

    [Fact]
    public void None_IsTheZeroedStartingPoint()
    {
        DownloadProgress.None.ReceivedBytes.ShouldBe(0);
        DownloadProgress.None.TotalBytes.ShouldBeNull();
        DownloadProgress.None.BytesPerSecond.ShouldBe(0d);
    }
}