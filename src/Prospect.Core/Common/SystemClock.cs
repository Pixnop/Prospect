namespace Prospect.Core.Common;

/// <summary>
/// Implémentation d'<see cref="IClock"/> adossée à l'horloge système. C'est celle-ci que
/// la composition root de l'application enregistre ; les tests utilisent une fausse horloge.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}