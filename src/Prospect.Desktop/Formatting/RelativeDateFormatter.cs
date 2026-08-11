using System.Globalization;

namespace Prospect.Desktop.Formatting;

/// <summary>
/// Formate une date en expression relative française ("aujourd'hui", "hier", "il y a N jours"),
/// comme l'exige la voix du produit (design/readme.md, section « Content fundamentals ») :
/// "Human time is written the human way". Classe statique PURE, qui reçoit l'instant courant en
/// paramètre plutôt que d'appeler <see cref="DateTimeOffset.UtcNow"/> ou
/// <see cref="Prospect.Core.Common.IClock"/> elle-même : c'est ce qui la rend testable par des
/// assertions exactes plutôt qu'approximatives (même principe que <see cref="Prospect.Core.Common.IClock"/>
/// côté Core, appliqué ici à une simple fonction plutôt qu'à un service).
/// </summary>
public static class RelativeDateFormatter
{
    /// <summary>Au-delà de ce nombre de jours révolus, on bascule sur une date absolue plutôt qu'un compte interminable.</summary>
    private const int MaxRelativeDays = 30;

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// Formate <paramref name="value"/> relativement à <paramref name="now"/>.
    /// <see langword="null"/> (instance jamais lancée) rend "jamais". Une date future (horloge
    /// système décalée, fuseau...) est tolérée plutôt que de produire une expression absurde
    /// ("il y a -2 jours") : le même jour UTC que <paramref name="now"/> rend "aujourd'hui",
    /// au-delà on retombe sur une date absolue.
    /// </summary>
    public static string Format(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value is null)
        {
            return "jamais";
        }

        var target = value.Value;
        var dayDifference = (now.UtcDateTime.Date - target.UtcDateTime.Date).Days;

        return dayDifference switch
        {
            0 => "aujourd'hui",
            1 => "hier",
            > 1 and <= MaxRelativeDays => $"il y a {dayDifference} jours",
            _ => FormatAbsolute(target),
        };
    }

    private static string FormatAbsolute(DateTimeOffset value) => value.UtcDateTime.ToString("d MMMM yyyy", French);
}