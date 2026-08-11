using Prospect.Core.Common;

using Shouldly;

namespace Prospect.Core.Tests.Common;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_EstProcheDeLHeureSysteme()
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
    public void UtcNow_EstMonotoneEntreDeuxLectures()
    {
        var clock = new SystemClock();

        var first = clock.UtcNow;
        var second = clock.UtcNow;

        // Le temps ne remonte jamais : la seconde lecture ne peut pas précéder la première.
        second.ShouldBeGreaterThanOrEqualTo(first);
    }

    [Fact]
    public void UtcNow_ARenvoieUnOffsetZero()
    {
        var clock = new SystemClock();

        var reading = clock.UtcNow;

        // UtcNow doit toujours produire un offset nul : c'est bien du temps UTC, pas une
        // heure locale déguisée.
        reading.Offset.ShouldBe(TimeSpan.Zero);
    }
}