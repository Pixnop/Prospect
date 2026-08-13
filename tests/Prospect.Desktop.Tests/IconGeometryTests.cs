using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Prospect.Desktop.Views.Shell;

using Shouldly;

namespace Prospect.Desktop.Tests;

/// <summary>
/// Les icônes du design system se consomment dans un <c>Path</c> à <c>Stretch</c>, qui met la
/// géométrie à l'échelle de la boîte du contrôle EN DIVISANT par les deux côtés de sa boîte
/// englobante. Une géométrie d'un seul trait droit a un côté NUL : Avalonia laisse alors le facteur
/// correspondant à zéro, <c>Uniform</c> retient le plus petit des deux facteurs, et l'icône entière
/// s'écrase sur un point. Rien ne le signale — ni compilation, ni exception, ni test de thème : le
/// bouton s'affiche simplement vide. C'est le défaut qu'a remonté la session de test Windows sur le
/// bouton Réduire, et ces deux gardes le tiennent des deux côtés : la source (aucune géométrie
/// dégénérée dans le dictionnaire) et l'usage (le trait du bouton Réduire couvre vraiment sa
/// surface après mise en page).
/// </summary>
public sealed class IconGeometryTests
{
    private const string IconsDictionaryUri = "avares://Prospect.Desktop/Styles/Icons.axaml";

    /// <summary>
    /// La garde de SOURCE, généralisée à tout le jeu d'icônes : une géométrie à côté nul ne se
    /// verrait autrement qu'à l'œil, sur la seule plateforme où quelqu'un regarde le bouton.
    /// </summary>
    [AvaloniaFact]
    public void NoIconGeometry_HasADegenerateSide()
    {
        var icons = LoadIcons();

        icons.Length.ShouldBeGreaterThan(30, "le dictionnaire d'icônes doit être chargé pour que cette garde prouve quoi que ce soit");

        var degenerate = icons
            .Where(icon => icon.Bounds.Width <= 0 || icon.Bounds.Height <= 0)
            .Select(icon => $"{icon.Key} ({icon.Bounds.Width} x {icon.Bounds.Height})")
            .Order(StringComparer.Ordinal)
            .ToArray();

        degenerate.ShouldBeEmpty(
            "Une géométrie d'un côté nul s'écrase sur un point sous n'importe quel Stretch. "
            + "Décrire l'empreinte peinte du trait (une pilule, consommée en Fill) plutôt que son axe, "
            + $"comme le fait Icon.minus. Fautives : {string.Join(", ", degenerate)}");
    }

    /// <summary>
    /// La garde d'USAGE : le trait du bouton Réduire, mesuré après mise en page réelle dans la barre
    /// de titre. Avant correction, la boîte peinte mesurait 1.75 x 1.75 (une pastille) au lieu de
    /// couvrir la largeur de l'icône.
    /// </summary>
    [AvaloniaFact]
    public void MinimizeButtonGlyph_CoversItsIconBox_RatherThanCollapsingToADot()
    {
        var titlebar = new TitlebarView();
        var window = new Window { Content = titlebar, Width = 640, Height = 200 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();

        // Le premier bouton de la barre est Réduire (ordre du design : réduire, agrandir, fermer).
        var glyph = titlebar.GetVisualDescendants().OfType<Button>().First().GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First();
        var painted = glyph.RenderedGeometry.ShouldNotBeNull().Bounds;

        painted.Width.ShouldBeGreaterThan(12d, "le trait du bouton Réduire doit couvrir la largeur de sa boîte d'icône");
        painted.Height.ShouldBeGreaterThan(1d, "le trait du bouton Réduire doit avoir une épaisseur, pas une hauteur nulle");

        // Et il tombe bien au centre du bouton, sans quoi il se collerait en haut de son cadre.
        var button = titlebar.GetVisualDescendants().OfType<Button>().First();
        var glyphCenter = glyph.TranslatePoint(glyph.Bounds.Center - glyph.Bounds.Position, button)!.Value;
        Math.Abs(glyphCenter.Y - button.Bounds.Height / 2d).ShouldBeLessThan(1.5d);

        window.Close();
    }

    private static (string Key, Rect Bounds)[] LoadIcons()
    {
        var uri = new Uri(IconsDictionaryUri);
        var dictionary = new ResourceInclude(uri) { Source = uri }.Loaded;

        return dictionary.Keys
            .Select(key => (Key: key.ToString() ?? "?", Value: dictionary.TryGetResource(key, null, out var value) ? value : null))
            .Where(entry => entry.Value is Geometry)
            .Select(entry => (entry.Key, ((Geometry)entry.Value!).Bounds))
            .ToArray();
    }
}