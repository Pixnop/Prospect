using Prospect.Core.Common;

namespace Prospect.Core.Tests.Common;

/// <summary>
/// Double de test d'<see cref="IClock"/> : heure entièrement contrôlée par le test plutôt que
/// l'horloge système, pour des assertions exactes sur les horodatages posés par les services du
/// Core (création d'instance, migration, playtime...).
/// </summary>
internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;

    public DateTimeOffset UtcNow { get; set; }
}