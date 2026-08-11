using Prospect.Core.Common;

namespace Prospect.Desktop.Formatting;

/// <summary>
/// Fait correspondre <see cref="GameVersionChannel"/> (vocabulaire du Core) aux quatre teintes
/// de badge du design (<c>stable</c>, <c>unstable</c>, <c>pre</c>, <c>incompatible</c> — voir
/// <c>Styles/Controls/Badge.axaml</c>). <c>incompatible</c> n'a pas d'équivalent ici : c'est une
/// relation entre un mod et une version, pas un canal propre à une <see cref="GameVersion"/>,
/// donc aucune valeur de l'énumération n'y mappe. <see cref="GameVersionChannel.Rc"/> et
/// <see cref="GameVersionChannel.Dev"/> rejoignent tous deux <c>unstable</c> : le design ne
/// prévoit que trois teintes pour les canaux de version (stable/unstable/pre) et un candidat
/// (rc) comme un build de développement (dev) sont l'un et l'autre moins mûrs qu'une pre-release,
/// donc logés dans le même panier plutôt que d'inventer une cinquième teinte hors design.
/// </summary>
public static class ChannelBadgePresentation
{
    public const string Stable = "stable";
    public const string Unstable = "unstable";
    public const string Pre = "pre";

    public static string ToBadgeTone(GameVersionChannel channel) => channel switch
    {
        GameVersionChannel.Stable => Stable,
        GameVersionChannel.Pre => Pre,
        GameVersionChannel.Rc => Unstable,
        GameVersionChannel.Dev => Unstable,
        _ => Stable,
    };
}