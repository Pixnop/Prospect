using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.Tests.Support;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;

using Shouldly;

// Le pictogramme de repli est une forme Avalonia, pas System.IO.Path que les usings implicites
// amènent sous le même nom.
using IconPath = Avalonia.Controls.Shapes.Path;

namespace Prospect.Desktop.Tests.Mods;

/// <summary>
/// Les vignettes hors du navigateur de mods, montées dans le shell réel : ce qui est VRAIMENT
/// rendu, pas ce qu'un booléen de ViewModel annonce. C'est la différence qui compte ici, parce que
/// la tuile porte deux enfants superposés et qu'un <c>IsVisible</c> inversé se lirait pareil dans
/// les deux sens côté ViewModel.
/// </summary>
public sealed class ModLogoHeadlessTests
{
    /// <summary>
    /// Le défaut rapporté : la rangée d'un mod venu du ModDB montrait la caisse générique. Le
    /// catalogue est relevé par le navigateur, comme dans la vie, puis l'onglet s'en sert sans rien
    /// redemander — même client singleton, même cache.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task ModsTab_ModVenuDuModDb_RendSonLogoEtPasLaCaisse(string variant)
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var window = ResponsiveScenario.ShowWindow(provider, variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.21.3");
        ResponsiveScenario.SeedModDbMod(provider, fileSystem, record.Slug, "betterruins", "BetterRuins", modDbModId: 792);

        // Le navigateur relève le catalogue, comme le ferait n'importe quelle session qui a déjà
        // installé un mod : c'est lui, et lui seul, qui remplit le cache dont vit l'annuaire.
        shell.ShowModBrowser(record.Slug);
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);

        var detail = await OpenModsTabAsync(shell, record.Slug, window);
        var row = detail.ModsTab.Mods.ShouldHaveSingleItem();
        await row.Thumbnail.LoadCompletion;
        window.Settle();

        row.IsFromModDb.ShouldBeTrue();
        row.Thumbnail.HasLogo.ShouldBeTrue("le catalogue mémorisé annonce un logo pour la fiche 792");

        var tile = RowTiles(window).ShouldHaveSingleItem();
        tile.Image.IsVisible.ShouldBeTrue("le logo décodé doit remplacer le pictogramme");
        tile.Image.Source.ShouldNotBeNull();
        tile.Fallback.IsVisible.ShouldBeFalse("les deux enfants de la tuile ne s'affichent jamais ensemble");

        window.Close();
    }

    /// <summary>
    /// Un zip déposé à la main garde la caisse : c'est un état honnête, cohérent avec le badge de
    /// provenance affiché sur la même rangée, et non un logo qui aurait échoué à se charger.
    /// </summary>
    [AvaloniaFact]
    public async Task ModsTab_ModDeposeALaMain_GardeLeMemePictogrammeQuAvant()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.21.3");
        ResponsiveScenario.SeedInstalledMod(provider, fileSystem, record.Slug);

        shell.ShowModBrowser(record.Slug);
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);

        var detail = await OpenModsTabAsync(shell, record.Slug, window);
        var row = detail.ModsTab.Mods.ShouldHaveSingleItem();
        await row.Thumbnail.LoadCompletion;
        window.Settle();

        row.IsFromModDb.ShouldBeFalse();

        var tile = RowTiles(window).ShouldHaveSingleItem();
        tile.Fallback.IsVisible.ShouldBeTrue();
        tile.Image.IsVisible.ShouldBeFalse();

        window.Close();
    }

    /// <summary>
    /// HORS LIGNE : aucune erreur, aucun trou de mise en page, et la liste s'affiche entière. Elle
    /// vit d'un scan disque, la vignette n'est qu'un enrichissement.
    /// </summary>
    [AvaloniaFact]
    public async Task ModsTab_HorsLigne_ListeQuandMemeAvecLePictogramme()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out var catalogHandler);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.21.3");
        ResponsiveScenario.SeedModDbMod(provider, fileSystem, record.Slug, "betterruins", "BetterRuins", modDbModId: 792);
        catalogHandler.ModDb.IsOnline = false;

        var detail = await OpenModsTabAsync(shell, record.Slug, window);
        var row = detail.ModsTab.Mods.ShouldHaveSingleItem();
        await row.Thumbnail.LoadCompletion;
        window.Settle();

        detail.ModsTab.HasMods.ShouldBeTrue();
        row.Thumbnail.HasLogo.ShouldBeFalse();

        var tile = RowTiles(window).ShouldHaveSingleItem();
        tile.Fallback.IsVisible.ShouldBeTrue();
        tile.Fallback.Bounds.Width.ShouldBeGreaterThan(0d, "le repli occupe la place, il ne laisse pas un trou");

        window.Close();
    }

    /// <summary>
    /// La tuile ne bouge pas d'un point selon qu'elle porte un logo ou un pictogramme, aux trois
    /// tailles de la garde : c'est ce qui garantit qu'un chargement qui aboutit en retard ne fait
    /// pas sauter la ligne sous le curseur.
    /// </summary>
    [AvaloniaFact]
    public async Task ModsTab_LaTuileGardeSaBoiteAvecOuSansLogo()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.21.3");
        ResponsiveScenario.SeedModDbMod(provider, fileSystem, record.Slug, "betterruins", "BetterRuins", modDbModId: 792);
        ResponsiveScenario.SeedInstalledMod(provider, fileSystem, record.Slug);

        shell.ShowModBrowser(record.Slug);
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);

        var detail = await OpenModsTabAsync(shell, record.Slug, window);
        foreach (var row in detail.ModsTab.Mods)
        {
            await row.Thumbnail.LoadCompletion;
        }

        detail.ModsTab.Mods.Count.ShouldBe(2);
        detail.ModsTab.Mods.Count(row => row.Thumbnail.HasLogo).ShouldBe(1);

        foreach (var size in ResponsiveWindowSizes.All)
        {
            window.Width = size.Width;
            window.Height = size.Height;
            window.Settle();

            var tiles = RowTiles(window).ToArray();
            tiles.Length.ShouldBe(2, $"deux rangées, deux tuiles, à {size}");
            tiles.Select(tile => tile.Border.Bounds.Size).Distinct().Count()
                .ShouldBe(1, $"la tuile change de taille selon qu'elle porte un logo, à {size}");
        }

        window.ShouldHoldLayoutInvariantsAtEverySize("Détail d'instance, onglet Mods, une rangée avec logo et une sans");

        window.Close();
    }

    private static async Task<InstanceDetailViewModel> OpenModsTabAsync(ShellViewModel shell, string slug, Window window)
    {
        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        detail.SelectTabCommand.Execute(InstanceDetailTab.Mods);
        window.Settle();

        return detail;
    }

    // La tuile d'une rangée de l'onglet Mods : 30 points de côté, un pictogramme et une image
    // superposés dans un Panel. La largeur suffit à la distinguer de la tuile de 40 du navigateur
    // et de celle de 52 de la fiche.
    private static IEnumerable<(Border Border, IconPath Fallback, Image Image)> RowTiles(Window window)
        => window
            .GetVisualDescendants()
            .OfType<Panel>()
            .Where(panel => panel.Parent is Border { Width: 30d } && panel.GetType() == typeof(Panel))
            .Select(panel => (
                Border: (Border)panel.Parent!,
                Fallback: panel.Children.OfType<IconPath>().Single(),
                Image: panel.Children.OfType<Image>().Single()));
}