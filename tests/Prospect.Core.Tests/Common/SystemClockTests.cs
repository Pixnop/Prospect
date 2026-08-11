using Prospect.Core.Common;

using Shouldly;

namespace Prospect.Core.Tests.Common;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_IsWithinSystemClockWindow()
    {
        var clock = new SystemClock();

        var before = DateTimeOffset.UtcNow;
        var reading = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        // La lecture doit tomber dans la fenêtre encadrée par deux appels directs à
        // DateTimeOffset.UtcNow, à la milliseconde de résolution près.
        reading.ShouldBeInRange(before.AddMilliseconds(-1), after.AddMilliseconds(1));
    }

    [Fact]
    public void UtcNow_HasZeroOffset()
    {
        var clock = new SystemClock();

        var reading = clock.UtcNow;

        // UtcNow doit toujours produire un offset nul : c'est bien du temps UTC, pas une
        // heure locale déguisée.
        reading.Offset.ShouldBe(TimeSpan.Zero);
    }
}