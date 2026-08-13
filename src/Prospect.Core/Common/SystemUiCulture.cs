using System.Globalization;

namespace Prospect.Core.Common;

/// <summary>
/// Implémentation d'<see cref="IUiCulture"/> adossée au système réel : le seul endroit du Core qui
/// a le droit de lire <see cref="CultureInfo.CurrentUICulture"/>, exactement comme
/// <see cref="SystemClock"/> est le seul à lire l'heure. C'est celle-ci que la composition root de
/// l'application enregistre ; les tests injectent un double.
/// </summary>
public sealed class SystemUiCulture : IUiCulture
{
    /// <inheritdoc />
    public string Name => CultureInfo.CurrentUICulture.Name;
}