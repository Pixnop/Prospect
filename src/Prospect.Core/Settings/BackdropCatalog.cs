namespace Prospect.Core.Settings;

/// <summary>
/// Vocabulaire des fonds de fenêtre embarqués : la liste close des clés qu'un
/// <see cref="ProspectSettings.Backdrop"/> peut valoir, dans l'ordre où l'écran Réglages les
/// propose. Pendant de <see cref="ThemePreference"/> pour l'autre réglage d'apparence — un pur
/// choix utilisateur, sans la moindre référence à Avalonia.
/// </summary>
/// <remarks>
/// <para>
/// Une liste de chaînes plutôt qu'une énumération, à la différence de
/// <see cref="ThemePreference"/>, parce que ces clés nomment des FICHIERS : chaque entrée
/// correspond à <c>Assets/Backdrops/&lt;clé&gt;.jpg</c> côté Desktop (voir
/// <c>Prospect.Desktop.Services.BackdropService</c>), et ajouter un fond doit rester le geste
/// « je dépose une image et j'ajoute sa clé ici ». Une énumération obligerait en plus à maintenir
/// une table clé → nom de fichier, pour la même information écrite deux fois.
/// </para>
/// <para>
/// Ce que le Core connaît s'arrête là : la clé et son rang. Le chemin de l'asset et le nom affiché
/// (traduit) sont des faits d'interface, ils vivent côté Desktop. Le Core, lui, a besoin de cette
/// liste pour une seule raison, mais elle est décisive : <see cref="ProspectSettings.Normalized"/>
/// doit pouvoir réparer une clé inconnue relue du disque, ce qu'il ne saurait pas faire sans savoir
/// ce qui existe.
/// </para>
/// </remarks>
public static class BackdropCatalog
{
    /// <summary>
    /// Fond par défaut : la capture livrée avec le thème verre (PR 38), donc celui que voit une
    /// installation qui ne touche à rien, avant comme après l'arrivée du sélecteur. C'est aussi le
    /// repli de toute clé absente ou inconnue, voir <see cref="ProspectSettings.NormalizeBackdrop"/>.
    /// </summary>
    public const string Default = "turquoise-pools";

    /// <summary>
    /// Les onze fonds embarqués, dans l'ordre d'affichage du sélecteur : le défaut d'abord, puis
    /// les extérieurs et enfin les intérieurs, du plus clair au plus sombre. L'ordre est une
    /// donnée de présentation assumée ici plutôt que recalculée côté vue, pour qu'il reste le même
    /// dans les deux langues et dans les tests.
    /// </summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        Default,
        "ruins-clearing",
        "sunlit-hills",
        "village-lane",
        "lake-sail",
        "dusk-reeds",
        "misty-yard",
        "reading-room",
        "stone-cellar",
        "overgrown-ruin",
        "crystal-vein",
    ];

    /// <summary>
    /// Vrai si <paramref name="key"/> est une clé du catalogue, comparaison insensible à la casse
    /// et aux espaces autour (mêmes tolérances que <see cref="ProspectSettings.NormalizeLanguage"/>).
    /// </summary>
    public static bool Contains(string? key) => Find(key) is not null;

    /// <summary>
    /// La clé canonique correspondant à <paramref name="key"/>, ou <see langword="null"/> si le
    /// catalogue ne la connaît pas. Rend toujours la forme du catalogue, pas celle reçue : un
    /// <c>prospect.json</c> qui porterait <c>Village-Lane</c> se relit en <c>village-lane</c>.
    /// </summary>
    public static string? Find(string? key)
    {
        var trimmed = key?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? null
            : Keys.FirstOrDefault(candidate => string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}