using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.Tests.Support;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Shell;

using Shouldly;

namespace Prospect.Desktop.Tests.Mods;

/// <summary>
/// La rangée de catégories du navigateur de mods : sa barre de défilement, et la molette.
/// </summary>
/// <remarks>
/// Les deux défauts corrigés ici étaient invisibles pour l'arpenteur d'invariants
/// (<see cref="LayoutInvariantWalker"/>), et il vaut mieux dire pourquoi : une barre de défilement
/// naît du GABARIT du ScrollViewer, donc elle porte un <c>TemplatedParent</c>, donc les deux
/// invariants qui auraient pu la prendre (débordement hors du parent, recouvrement entre frères)
/// l'exemptent par construction. C'est justifié — un gabarit superpose légitimement ses parties —
/// mais ça laisse un angle mort qu'il faut mesurer à la main, ce que fait ce fichier.
/// </remarks>
public sealed class TagRowScrollTests
{
    private const int CatalogSize = 8_000;
    private const int TagVocabularySize = 223;

    private static async Task<(ServiceProvider Provider, MainWindow Window, ScrollViewer Row)> OpenBrowserAsync()
    {
        var provider = TestServiceProviderFactory.Create(out _, out var handler);
        handler.ModDb.CatalogJson = LargeCatalog.CatalogJson(CatalogSize);
        handler.ModDb.TagsJson = LargeCatalog.TagsJson(TagVocabularySize);

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        window.Width = 1280;
        window.Height = 800;
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        var row = window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(scroll => scroll.GetVisualDescendants().OfType<ContentControl>().Any(content => content.Classes.Contains("tag")));

        return (provider, window, row);
    }

    private static Rect BoxOf(Visual visual, Visual root)
        => new Rect(visual.Bounds.Size).TransformToAABB(visual.TransformToVisual(root)!.Value);

    /// <summary>
    /// La barre horizontale ne peint plus par-dessus les pastilles : la rangée lui réserve sa bande.
    /// </summary>
    /// <remarks>
    /// Mesuré le 2026-08-13 avant correction : la rangée occupait 30 points de haut, ses pastilles
    /// tout du long, et la barre se posait en plein milieu — la gouttière de 12 points, prévue pour
    /// écarter une barre du bord de la FENÊTRE, ne faisait ici que l'enfoncer davantage dans le
    /// contenu.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheHorizontalBar_DoesNotPaintOverTheTagPills()
    {
        var (provider, window, row) = await OpenBrowserAsync();
        using var _provider = provider;

        var bar = row.GetVisualDescendants().OfType<ScrollBar>()
            .Single(scroll => scroll.Orientation == Avalonia.Layout.Orientation.Horizontal && scroll.IsVisible);
        var barBox = BoxOf(bar, window);

        var pills = row.GetVisualDescendants().OfType<ContentControl>()
            .Where(content => content.Classes.Contains("tag"))
            .Select(content => BoxOf(content, window))
            .Where(box => box.Width > 0 && box.Height > 0)
            .ToList();

        pills.ShouldNotBeEmpty();
        foreach (var pill in pills)
        {
            var overlap = pill.Intersect(barBox);
            (overlap.Width <= 1 || overlap.Height <= 1).ShouldBeTrue(
                $"la barre {barBox} recouvre une pastille {pill} sur {overlap}");
        }

        window.Close();
    }

    /// <summary>
    /// Et elle ne déborde pas non plus de la rangée : la correction réserve de la place, elle ne
    /// pousse pas la barre sur ce qui suit.
    /// </summary>
    [AvaloniaFact]
    public async Task TheHorizontalBar_StaysInsideTheRow()
    {
        var (provider, window, row) = await OpenBrowserAsync();
        using var _provider = provider;

        var bar = row.GetVisualDescendants().OfType<ScrollBar>()
            .Single(scroll => scroll.Orientation == Avalonia.Layout.Orientation.Horizontal && scroll.IsVisible);

        var rowBox = BoxOf(row, window);
        var barBox = BoxOf(bar, window);

        barBox.Y.ShouldBeGreaterThanOrEqualTo(rowBox.Y - 1);
        barBox.Bottom.ShouldBeLessThanOrEqualTo(rowBox.Bottom + 1);

        // Et rien de la grille de cartes ne se trouve sous elle.
        var cards = window.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("card"))
            .Select(border => BoxOf(border, window))
            .Where(box => box.Height > 0)
            .ToList();

        cards.ShouldNotBeEmpty();
        cards.ShouldAllBe(card => card.Y >= barBox.Bottom - 1);

        window.Close();
    }

    /// <summary>La rangée tient toujours sur UNE ligne : réserver la bande ne l'a pas fait doubler.</summary>
    [AvaloniaFact]
    public async Task TheRow_StillHoldsASingleLine()
    {
        var (provider, window, row) = await OpenBrowserAsync();
        using var _provider = provider;

        var tallestTag = row.GetVisualDescendants().OfType<Button>().Max(button => button.Bounds.Height);
        row.Bounds.Height.ShouldBeLessThanOrEqualTo(tallestTag + 24);

        window.Close();
    }

    /// <summary>
    /// La molette fait défiler la rangée latéralement. C'est le second volet du défaut : sans ça, les
    /// deux cents catégories au-delà du premier écran ne s'atteignaient qu'à la barre.
    /// </summary>
    [AvaloniaFact]
    public async Task TheWheel_OverTheRow_ScrollsItSideways()
    {
        var (provider, window, row) = await OpenBrowserAsync();
        using var _provider = provider;

        row.Extent.Width.ShouldBeGreaterThan(row.Viewport.Width, "la rangée doit vraiment déborder");
        row.Offset.X.ShouldBe(0);

        var rowBox = BoxOf(row, window);
        var overTheRow = new Point(rowBox.X + (rowBox.Width / 2), rowBox.Y + 4);

        window.MouseWheel(overTheRow, new Vector(0, -1));
        window.Settle();

        row.Offset.X.ShouldBeGreaterThan(0, "un cran vers le bas fait avancer la rangée vers la droite");

        // Et le geste inverse la ramène.
        var advanced = row.Offset.X;
        window.MouseWheel(overTheRow, new Vector(0, 1));
        window.Settle();

        row.Offset.X.ShouldBeLessThan(advanced);

        window.Close();
    }

    /// <summary>
    /// Arrivée en butée, la rangée ne consomme plus la molette : c'est la page qui reprend la main,
    /// et non un geste qui ne fait plus rien.
    /// </summary>
    [AvaloniaFact]
    public async Task TheWheel_AtTheStartOfTheRow_LeavesTheEventToThePage()
    {
        var (provider, window, row) = await OpenBrowserAsync();
        using var _provider = provider;

        var results = window.GetVisualDescendants().OfType<ScrollViewer>().Single(scroll => scroll.Name == "ResultsScroll");
        results.Offset.Y.ShouldBe(0);

        var rowBox = BoxOf(row, window);
        var overTheRow = new Point(rowBox.X + (rowBox.Width / 2), rowBox.Y + 4);

        // Vers la gauche alors qu'on y est déjà : rien à défiler ici.
        window.MouseWheel(overTheRow, new Vector(0, 1));
        window.Settle();

        row.Offset.X.ShouldBe(0);

        window.Close();
    }
}