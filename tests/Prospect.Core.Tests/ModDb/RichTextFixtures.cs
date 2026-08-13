using System.Reflection;

namespace Prospect.Core.Tests.ModDb;

/// <summary>
/// Échantillons de HTML RÉEL, relevés sur le ModDB le 2026-08-13 et embarqués tels quels.
/// </summary>
/// <remarks>
/// Le fichier compte 29 Ko et une douzaine de balises distinctes (154 <c>p</c>, 256 <c>li</c>,
/// 226 <c>br</c>, 120 <c>ul</c>, 62 titres, 34 liens, 4 images hébergées sur S3, du
/// <c>pre</c>/<c>code</c>, du <c>span</c> et du <c>div</c> à dégrader). Aucun échantillon écrit à
/// la main n'a cette forme : les descriptions de fiches ne sortent pas d'un générateur, elles
/// sortent d'un éditeur WYSIWYG utilisé par des joueurs pendant des années, et c'est précisément
/// cet écart-là que le parseur doit encaisser.
/// </remarks>
internal static class RichTextFixtures
{
    /// <summary>
    /// Le champ <c>text</c> de <c>https://mods.vintagestory.at/api/mod/carryon</c>, tel que servi.
    /// </summary>
    public static string CarryOnDescriptionHtml { get; } = Read("carryon-description.html");

    /// <summary>
    /// Le <c>changelog</c> de la release la plus récente de Carry On au moment du relevé. Il est
    /// en HTML lui aussi, ce que rien dans l'API ne dit : c'est ce qui justifie de le faire passer
    /// par le même parseur que la description plutôt que de l'afficher tel quel.
    /// </summary>
    public const string CarryOnChangelogHtml =
        "<ul><li>Block trunks and collapsed chests from being attached to boats.</li>\n"
        + "<li>Fixed containers, attached to boats, that had never been opened losing their storage slots"
        + " when detached and re-attached.</li>\n"
        + "<li>Fixed attaching items to occupied seats on vehicles like Cartwright's Caravan.</li>\n"
        + "<li>Spectators no longer see carry HUD and carried blocks visuals while observing.</li></ul>";

    private static string Read(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(candidate => candidate.EndsWith(name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
