using System.IO.Abstractions;

using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Storage;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.Views.Mods;
using Prospect.Tests.Live;

using Shouldly;

using Xunit.Abstractions;

namespace Prospect.Desktop.Tests.Live;

/// <summary>
/// Le navigateur de mods monté sur la VRAIE composition root, sans aucun double : système de
/// fichiers réel (dans un dossier temporaire), pile réseau réelle, catalogue réel. C'est le seul
/// test de tout le dépôt où l'application parle au vrai ModDB par le chemin de production, et donc
/// le seul capable de reproduire ce que le propriétaire voit en compilant depuis les sources.
/// </summary>
[Trait("Category", "Live")]
public sealed class ModBrowserLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prospect-live-shell-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Dossier temporaire non supprimé : sans conséquence sur le verdict du test.
        }
    }

    [LiveAvaloniaFact]
    public async Task RealBrowser_LoadsTheRealCatalogThenOpensAModWithoutBlowingUp()
    {
        using var provider = CreateRealProvider();
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        shell.ShowModBrowser();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        var loaded = watch.ElapsedMilliseconds;

        watch.Restart();
        window.Settle();
        output.WriteLine(
            $"Catalogue réel chargé en {loaded} ms, {shell.ModBrowser.Results.Count} résultats, "
            + $"{shell.ModBrowser.Tags.Count} tags ; première mise en page en {watch.ElapsedMilliseconds} ms.");

        shell.ModBrowser.IsOffline.ShouldBeFalse();
        shell.ModBrowser.Results.ShouldNotBeEmpty();

        // Le clic lui-même, par la commande de la carte : c'est ce chemin exact qui plantait.
        var card = shell.ModBrowser.Results[0];
        output.WriteLine($"Clic sur « {card.Name} » (modid {card.ModId}).");
        await card.OpenCommand.ExecuteAsync(null);
        window.Settle();

        shell.Overlay.Active.ShouldBeOfType<ModDetailDialogViewModel>();
        window.GetVisualDescendants().OfType<ModDetailDialogView>().ShouldNotBeEmpty();

        // La grille ne matérialise qu'une fenêtre de cartes : c'est ce qui rend la première mise en
        // page tenable sur 8 000 fiches, et ce qui borne le nombre de logos téléchargés.
        shell.ModBrowser.MatchCount.ShouldBeGreaterThan(ModBrowserViewModel.RenderWindowSize);
        shell.ModBrowser.Results.Count.ShouldBe(ModBrowserViewModel.RenderWindowSize);

        window.Close();
    }

    // La composition root de production, à deux détails près : la racine disque est un dossier
    // temporaire (sinon le test écraserait le cache de l'utilisateur) et rien d'autre n'est
    // substitué — surtout pas le gestionnaire HTTP, qui reste celui de la pile réseau par défaut.
    private ServiceProvider CreateRealProvider()
    {
        var services = new ServiceCollection();
        CompositionRoot.ConfigureServices(services, new FileSystem());
        services.AddSingleton(new AppPaths(new SystemAppEnvironment(), _root));

        return services.BuildServiceProvider();
    }
}