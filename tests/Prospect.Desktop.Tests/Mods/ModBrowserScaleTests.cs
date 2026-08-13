using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.Support;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;

using Shouldly;

namespace Prospect.Desktop.Tests.Mods;

/// <summary>
/// Le navigateur de mods à l'échelle du catalogue RÉEL, sur des doubles. Ce que ces tests gardent
/// ne se voit sur aucun catalogue de trois mods : le nombre de cartes réellement matérialisées, le
/// nombre de logos réellement téléchargés, et la taille de la rangée de catégories.
/// </summary>
/// <remarks>
/// Le générateur (<see cref="LargeCatalog"/>) reproduit la forme mesurée en conditions réelles
/// (docs/research/moddb-api.md et l'étage live de cette PR) : 8 000 fiches d'un seul bloc, deux
/// tiers avec un logo, un vocabulaire de plus de deux cents catégories, et des vignettes aux
/// dimensions du CDN. C'est cette forme-là, et pas une autre, qui faisait matérialiser 250 000
/// visuels et décoder plusieurs gigaoctets de pixels.
/// </remarks>
public sealed class ModBrowserScaleTests
{
    private const int CatalogSize = 8_000;
    private const int TagVocabularySize = 223;

    /// <summary>
    /// Plafond du nombre de cartes présentes dans l'arbre visuel. Généreux par rapport à la fenêtre
    /// de rendu : ce qui compte est qu'il soit BORNÉ et sans rapport avec la taille du catalogue,
    /// pas qu'il vaille exactement une valeur.
    /// </summary>
    private const int MaxRealizedCards = 4 * ModBrowserViewModel.RenderWindowSize;

    private static (ServiceProvider Provider, MainWindow Window, ShellViewModel Shell, FakeCatalogHandler Handler) CreateAtScale()
    {
        var provider = TestServiceProviderFactory.Create(out _, out var handler);
        handler.ModDb.CatalogJson = LargeCatalog.CatalogJson(CatalogSize);
        handler.ModDb.TagsJson = LargeCatalog.TagsJson(TagVocabularySize);

        return (provider, provider.GetRequiredService<MainWindow>(), provider.GetRequiredService<ShellViewModel>(), handler);
    }

    private static int CountCards(MainWindow window)
        => window.GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("card"));

    [AvaloniaFact]
    public async Task WholeCatalog_RealizesOnlyABoundedWindowOfCards()
    {
        var (provider, window, shell, _) = CreateAtScale();
        using var _provider = provider;
        window.Show();
        shell.ShowModBrowser();

        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        // Les DONNÉES restent complètes : c'est le rendu qui est paresseux, pas la recherche.
        shell.ModBrowser.MatchCount.ShouldBe(CatalogSize);
        shell.ModBrowser.Results.Count.ShouldBe(ModBrowserViewModel.RenderWindowSize);
        shell.ModBrowser.HasMoreResults.ShouldBeTrue();
        CountCards(window).ShouldBeLessThanOrEqualTo(MaxRealizedCards);
    }

    [AvaloniaFact]
    public async Task WholeCatalog_DownloadsALogoOnlyForTheCardsItRealizes()
    {
        var (provider, window, shell, handler) = CreateAtScale();
        using var _provider = provider;
        window.Show();
        shell.ShowModBrowser();

        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        foreach (var card in shell.ModBrowser.Results)
        {
            await card.LogoLoadCompletion;
        }

        window.Settle();

        // Une carte sur trois du générateur n'a pas de logo : la borne est donc largement au-dessus
        // de ce qui devrait partir, et infiniment en dessous des 5 000 requêtes qu'une grille non
        // fenêtrée déclenchait sur le catalogue réel.
        handler.ModDb.LogoRequestCount.ShouldBeLessThanOrEqualTo(ModBrowserViewModel.RenderWindowSize);
    }

    [AvaloniaFact]
    public async Task ShowMore_ExtendsTheWindowOneSliceAtATime()
    {
        var (provider, window, shell, _) = CreateAtScale();
        using var _provider = provider;
        window.Show();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        var first = shell.ModBrowser.Results[0];
        shell.ModBrowser.ShowMoreCommand.Execute(null);
        window.Settle();

        shell.ModBrowser.Results.Count.ShouldBe(2 * ModBrowserViewModel.RenderWindowSize);

        // Étendre n'est pas reconstruire : les cartes déjà présentes sont les mêmes objets, donc
        // leurs logos déjà chargés ne repartent pas en téléchargement.
        shell.ModBrowser.Results[0].ShouldBeSameAs(first);
        shell.ModBrowser.MatchCount.ShouldBe(CatalogSize);
    }

    [AvaloniaFact]
    public async Task ChangingTheSearch_ResetsTheWindowToItsFirstSlice()
    {
        var (provider, window, shell, _) = CreateAtScale();
        using var _provider = provider;
        window.Show();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        shell.ModBrowser.ShowMoreCommand.Execute(null);
        shell.ModBrowser.ShowMoreCommand.Execute(null);
        window.Settle();
        shell.ModBrowser.Results.Count.ShouldBe(3 * ModBrowserViewModel.RenderWindowSize);

        shell.ModBrowser.SearchText = "Mod de test 1";
        window.Settle();

        shell.ModBrowser.Results.Count.ShouldBe(ModBrowserViewModel.RenderWindowSize);
        shell.ModBrowser.MatchCount.ShouldBeGreaterThan(ModBrowserViewModel.RenderWindowSize);
        CountCards(window).ShouldBeLessThanOrEqualTo(MaxRealizedCards);
    }

    [AvaloniaFact]
    public async Task SortingAndFiltering_StillSeeTheWholeCatalogRatherThanTheRenderedWindow()
    {
        var (provider, window, shell, _) = CreateAtScale();
        using var _provider = provider;
        window.Show();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        // Le générateur décroît les téléchargements avec l'indice : trier par nom doit donc faire
        // remonter une fiche que la fenêtre initiale, triée par téléchargements, ne contenait pas.
        shell.ModBrowser.SortIndex = (int)Prospect.Core.ModDb.ModCatalogSort.Name;
        window.Settle();

        shell.ModBrowser.Results[0].Name.ShouldBe("Mod de test 1");
        shell.ModBrowser.MatchCount.ShouldBe(CatalogSize);
        shell.ModBrowser.Results.Count.ShouldBe(ModBrowserViewModel.RenderWindowSize);
    }

    [AvaloniaFact]
    public async Task TagVocabulary_StaysOnASingleRowOrderedByHowManyModsUseIt()
    {
        var (provider, window, shell, _) = CreateAtScale();
        using var _provider = provider;
        window.Show();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        shell.ModBrowser.Tags.Count.ShouldBe(TagVocabularySize);

        // Les catégories réellement portées par des mods passent devant celles que personne
        // n'emploie, quel que soit leur ordre alphabétique.
        var used = LargeCatalog.TagNames.Count;
        shell.ModBrowser.Tags.Take(used).Select(tag => tag.Name).ShouldBe(LargeCatalog.TagNames, ignoreOrder: true);

        // Une seule rangée : la zone de tags ne peut pas dépasser la hauteur d'un bouton de tag,
        // barre de défilement horizontale comprise.
        var row = window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(scroll => scroll.GetVisualDescendants().OfType<ContentControl>().Any(content => content.Classes.Contains("tag")));
        var tallestTag = row.GetVisualDescendants().OfType<Button>().Max(button => button.Bounds.Height);
        row.Bounds.Height.ShouldBeLessThanOrEqualTo(tallestTag + 24);
    }

    [AvaloniaFact]
    public async Task LongCatalogWithAnExtendedWindow_HoldsTheLayoutInvariants()
    {
        var (provider, window, shell, _) = CreateAtScale();
        using var _provider = provider;
        window.Show();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        shell.ModBrowser.ShowMoreCommand.Execute(null);
        shell.ModBrowser.ShowMoreCommand.Execute(null);
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize("navigateur de mods, catalogue complet, fenêtre étendue");
    }

    /// <summary>
    /// Le défilement ne peut pas matérialiser le catalogue entier une tranche à la fois. C'est la
    /// borne qui manquait : l'extension par tranches bornait le rendu INITIAL et rien d'autre, si
    /// bien qu'un défilement assez long rendait les 8 026 cartes, exactement ce que la fenêtre était
    /// censée éviter. Mesuré avant correction sur un catalogue de 800 fiches : 800 cartes rendues
    /// coûtaient 392 Mio de mémoire gérée (l'arbre de contrôles, environ 490 Kio par carte) et
    /// portaient le jeu de travail de 177 à 644 Mio.
    /// </summary>
    [AvaloniaFact]
    public async Task ScrollingFarBeyondTheWindow_StopsAtTheRenderCeilingAndSaysSo()
    {
        var (provider, window, shell, _) = CreateAtScale();
        using var _provider = provider;
        window.Show();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);

        // Bien plus de tranches qu'il n'en faut pour atteindre le plafond : un défilement obstiné.
        for (var slice = 0; slice < 40; slice++)
        {
            shell.ModBrowser.ShowMoreCommand.Execute(null);
        }

        window.Settle();

        shell.ModBrowser.Results.Count.ShouldBe(ModBrowserViewModel.MaxRenderedResults);
        CountCards(window).ShouldBeLessThanOrEqualTo(ModBrowserViewModel.MaxRenderedResults);

        // Les DONNÉES restent complètes, et l'écran ne fait pas semblant : plus de bouton
        // « Afficher plus », et un compteur qui dit par quoi remplacer le défilement.
        shell.ModBrowser.MatchCount.ShouldBe(CatalogSize);
        shell.ModBrowser.HasMoreResults.ShouldBeFalse();
        shell.ModBrowser.IsRenderCapped.ShouldBeTrue();
        shell.ModBrowser.ShownCountText.ShouldBe(
            UiText.Mods.ShownCountCapped(ModBrowserViewModel.MaxRenderedResults, CatalogSize));
    }

    /// <summary>
    /// Le plafond de rendu ne bride que le rendu : changer de recherche repart d'une tranche, et
    /// une recherche qui tient sous le plafond n'affiche aucun message de repli.
    /// </summary>
    [AvaloniaFact]
    public async Task RenderCeiling_LiftsAgainAsSoonAsTheSearchFitsUnderIt()
    {
        var (provider, window, shell, _) = CreateAtScale();
        using var _provider = provider;
        window.Show();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        for (var slice = 0; slice < 40; slice++)
        {
            shell.ModBrowser.ShowMoreCommand.Execute(null);
        }

        window.Settle();
        shell.ModBrowser.IsRenderCapped.ShouldBeTrue();

        // Une recherche qui ramène moins que le plafond : tout redevient atteignable.
        shell.ModBrowser.SearchText = "Mod de test 42";
        window.Settle();
        shell.ModBrowser.MatchCount.ShouldBeLessThan(ModBrowserViewModel.MaxRenderedResults);
        shell.ModBrowser.IsRenderCapped.ShouldBeFalse();

        for (var slice = 0; slice < 40; slice++)
        {
            shell.ModBrowser.ShowMoreCommand.Execute(null);
        }

        window.Settle();

        shell.ModBrowser.Results.Count.ShouldBe(shell.ModBrowser.MatchCount);
        shell.ModBrowser.HasMoreResults.ShouldBeFalse();
        shell.ModBrowser.IsRenderCapped.ShouldBeFalse();
        shell.ModBrowser.ShownCountText.ShouldBe(
            UiText.Mods.ShownCount(shell.ModBrowser.MatchCount, shell.ModBrowser.MatchCount));
    }

    /// <summary>
    /// Régression du plantage rapporté en conditions réelles : le catalogue entier servi avec des
    /// vignettes aux dimensions du CDN. Sans la réduction à la taille d'affichage, décoder ces
    /// images telles quelles faisait exploser la mémoire (mesuré à près de 8 Gio de jeu de travail
    /// pour 5 333 logos réels).
    /// </summary>
    [AvaloniaFact]
    public async Task CdnSizedLogos_AreShrunkToTheirDisplaySizeAndTheCacheStaysBounded()
    {
        var (provider, window, shell, handler) = CreateAtScale();
        using var _provider = provider;
        handler.ModDb.LogoBytes = TinyPng.Create(480);
        window.Show();
        shell.ShowModBrowser();

        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        foreach (var card in shell.ModBrowser.Results)
        {
            await card.LogoLoadCompletion;
        }

        window.Settle();

        var loaded = shell.ModBrowser.Results.Where(card => card.HasLogo).ToList();
        loaded.ShouldNotBeEmpty();
        loaded.ShouldAllBe(card => card.LogoBitmap!.PixelSize.Width <= ModLogoCache.MaxLogoWidth);

        var cache = provider.GetRequiredService<IModLogoCache>().ShouldBeOfType<ModLogoCache>();
        cache.CachedCount.ShouldBeLessThanOrEqualTo(ModLogoCache.MaxCachedBitmaps);
    }
}