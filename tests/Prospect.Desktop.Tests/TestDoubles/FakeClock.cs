using Prospect.Core.Common;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>Même rôle que son homonyme de Prospect.Core.Tests (assemblys distincts, pas de partage direct).</summary>
internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;

    public DateTimeOffset UtcNow { get; set; }
}