using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Http;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Settings;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.Views.Home;
using Prospect.Desktop.Views.Mods;

using Shouldly;

namespace Prospect.Desktop.Tests.Shell;

/// <summary>
/// Le shell s'instancie avec le conteneur DI réel sur un système de fichiers factice
/// (docs/architecture.md, exigence de test) et la navigation change effectivement de page.
/// </summary>
public class ShellHeadlessTests
{
    [AvaloniaFact]
    public void MainWindow_ResolvedViaRealDI_ShowsShellWithHomeActiveByDefault()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shellViewModel = provider.GetRequiredService<ShellViewModel>();

        window.Show();
        window.Settle();

        window.GetVisualDescendants().OfType<HomeView>().ShouldNotBeEmpty();
        shellViewModel.CurrentPage.ShouldBe(shellViewModel.Home);
        shellViewModel.LibraryNavItems[0].IsActive.ShouldBeTrue();
        shellViewModel.LibraryNavItems[1].IsActive.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public void SelectingModsNavItem_ShowsTheModBrowserAndUpdatesActiveState()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shellViewModel = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        window.Settle();

        var modsNavItem = shellViewModel.LibraryNavItems[1];
        modsNavItem.SelectCommand.Execute(null);
        window.Settle();

        shellViewModel.CurrentPage.ShouldBeOfType<ModBrowserViewModel>();
        modsNavItem.IsActive.ShouldBeTrue();
        shellViewModel.LibraryNavItems[0].IsActive.ShouldBeFalse();
        window.GetVisualDescendants().OfType<ModBrowserView>().ShouldNotBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public void SelectingSettingsThenBackToHome_RestoresHomeAsActivePage()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shellViewModel = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        window.Settle();

        shellViewModel.SettingsNavItem.SelectCommand.Execute(null);
        window.Settle();
        shellViewModel.CurrentPage.ShouldBeOfType<SettingsViewModel>();

        shellViewModel.LibraryNavItems[0].SelectCommand.Execute(null);
        window.Settle();

        shellViewModel.CurrentPage.ShouldBe(shellViewModel.Home);
        shellViewModel.LibraryNavItems[0].IsActive.ShouldBeTrue();
        shellViewModel.SettingsNavItem.IsActive.ShouldBeFalse();
        window.GetVisualDescendants().OfType<HomeView>().ShouldNotBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public void ToggleDownloadsPopover_OpensAndClosesWithoutChangingCurrentPage()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shellViewModel = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        window.Settle();
        var pageBefore = shellViewModel.CurrentPage;

        shellViewModel.ToggleDownloadsPopoverCommand.Execute(null);
        window.Settle();
        shellViewModel.IsDownloadsPopoverOpen.ShouldBeTrue();
        shellViewModel.CurrentPage.ShouldBe(pageBefore);

        shellViewModel.CloseDownloadsPopoverCommand.Execute(null);
        window.Settle();
        shellViewModel.IsDownloadsPopoverOpen.ShouldBeFalse();

        window.Close();
    }

    /// <summary>
    /// Conserver l'historique de la session a un effet de bord sur les deux compteurs du panneau :
    /// ils comptent ce qui TOURNE, et une file qui n'a plus que de l'historique les laissait
    /// afficher un « 0 » dans la barre latérale et une bande de pied vide sous les lignes. Le
    /// contenu, lui, doit rester : c'est tout l'intérêt de l'historique.
    /// </summary>
    [AvaloniaFact]
    public async Task DownloadsPopover_WithNothingLeftRunning_KeepsItsHistoryButHidesItsCounters()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        await provider.GetRequiredService<IDownloadManager>().DownloadAsync(new DownloadRequest(
            "Config lib 1.11.1",
            "configlib_1.11.1.zip",
            [new Uri("https://moddbcdn.vintagestory.at/configlib_1.11.1.zip")]));

        shell.ToggleDownloadsPopoverCommand.Execute(null);
        window.Settle();

        shell.Downloads.Items.Count.ShouldBe(1);
        shell.Downloads.HasDownloads.ShouldBeTrue();
        shell.Downloads.HasActive.ShouldBeFalse();

        var navItem = window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == "Téléchargements"));
        navItem.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == "0").IsVisible.ShouldBeFalse();

        window
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("glassBand"))
            .IsVisible.ShouldBeFalse();

        // La ligne d'historique, elle, est bien rendue : c'est la moitié qui doit rester.
        window
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Any(block => block.Text == "Config lib 1.11.1")
            .ShouldBeTrue();

        window.Close();
    }

    /// <summary>
    /// Les trois propriétés de décoration du chemin « barre de titre maison », figées ensemble.
    /// </summary>
    /// <remarks>
    /// Elles ne se suffisent pas l'une l'autre, et c'est l'oubli de la troisième qui donnait DEUX
    /// barres de titre superposées sur Windows : <c>ExtendClientAreaChromeHints</c> vaut
    /// <c>Default</c> tant qu'on ne l'écrit pas, et <c>Default</c> est un alias de
    /// <c>PreferSystemChrome</c> (métadonnées d'Avalonia 11.3.20 : la propriété est enregistrée
    /// avec la valeur 2, qui est celle de <c>PreferSystemChrome</c>). Étendre la zone client sans
    /// rien dire du chrome revient donc à demander au système de dessiner le sien par-dessus le
    /// nôtre. Aucun test headless ne peut voir le rendu Windows : ce test fige les propriétés, la
    /// vérification visuelle reste humaine.
    /// </remarks>
    [AvaloniaFact]
    public void MainWindow_OnTheCustomTitlebarPath_AsksThePlatformForNoChromeOfItsOwn()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shellViewModel = provider.GetRequiredService<ShellViewModel>();

        // Les trois OS sont sur le chemin « barre de titre maison » aujourd'hui (ShellViewModel) :
        // si cette bascule change un jour, ce test doit être relu, pas contourné.
        shellViewModel.UseCustomTitlebar.ShouldBeTrue();

        window.ExtendClientAreaToDecorationsHint.ShouldBeTrue();
        window.ExtendClientAreaChromeHints.ShouldBe(ExtendClientAreaChromeHints.NoChrome);
        window.ExtendClientAreaTitleBarHeightHint.ShouldBe(MainWindow.CustomTitleBarHeight);

        // BorderOnly privait la fenêtre du cadre non client de Windows (ombre, poignées de
        // redimensionnement, accrochage) sans rien masquer du chrome : ce n'était pas lui le
        // remède.
        window.SystemDecorations.ShouldBe(SystemDecorations.Full);
    }

    /// <summary>
    /// La hauteur annoncée à la plateforme et celle que dessine <c>TitlebarView</c> viennent de
    /// deux endroits différents (une constante C# et un jeton XAML) : une divergence ne se verrait
    /// qu'à l'exécution, sur la seule plateforme où la zone de légende native compte.
    /// </summary>
    [AvaloniaFact]
    public void CustomTitleBarHeight_MatchesTheDesignToken()
    {
        Application.Current!.TryFindResource("TitlebarH", out var token).ShouldBeTrue();
        token.ShouldBe(MainWindow.CustomTitleBarHeight);
    }
}