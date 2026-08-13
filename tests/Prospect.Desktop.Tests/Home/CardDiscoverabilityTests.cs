using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.Tests.Support;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.Views.Home;

using Shouldly;

namespace Prospect.Desktop.Tests.Home;

/// <summary>
/// « Aucun moyen de gérer une instance, lancer le docteur, gérer les mods installés. » Le chemin
/// existait pourtant en entier. Ces tests injectent de VRAIS clics de souris, aux endroits où
/// l'utilisateur les pose, pour séparer les deux diagnostics possibles : un chemin qui ne répond
/// pas, ou un chemin qu'on ne trouve pas.
///
/// Verdict : les trois clics passent. Le mécanisme n'avait rien : la carte d'accueil est un bouton
/// pleine surface, le « … » de l'en-tête ouvre son menu, « Vérifier l'instance » ouvre le docteur.
/// Ce qui manquait, c'est que rien ne DISAIT que la vignette était cliquable — l'utilisateur l'a
/// confirmé lui-même. Ces tests restent comme garde du mécanisme, et les deux derniers de la classe
/// gardent l'affordance qui a été ajoutée par-dessus.
/// </summary>
public sealed class CardDiscoverabilityTests
{
    private static void Click(Window window, Visual target, Point offset)
    {
        var point = target.TranslatePoint(offset, window).ShouldNotBeNull();
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Settle(window);
    }

    private static void Hover(Window window, Visual target, Point offset)
    {
        var point = target.TranslatePoint(offset, window).ShouldNotBeNull();
        window.MouseMove(point);
        Settle(window);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
    }

    /// <summary>
    /// Fait tourner l'horloge d'animation jusqu'à ce que la condition tienne. Les transitions du
    /// design system durent une fraction de seconde : lues sur la première image, elles sont encore
    /// à quelques pour cent de leur course. Ce n'est pas la vitesse qu'on veut épingler ici, c'est
    /// la destination.
    /// </summary>
    private static void SettleUntil(Window window, Func<bool> condition)
    {
        for (var frame = 0; frame < 600 && !condition(); frame++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        window.UpdateLayout();
    }

    private static async Task<(MainWindow Window, ShellViewModel Shell)> ShowPopulatedHomeAsync(
        ServiceProvider provider,
        System.IO.Abstractions.TestingHelpers.MockFileSystem fileSystem,
        ThemeVariant? theme = null)
    {
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider, theme);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.ShortInstanceName, "1.20.4");
        Settle(window);

        return (window, shell);
    }

    private static InstanceCardView CardOf(Window window)
        => window.GetVisualDescendants().OfType<InstanceCardView>().Single();

    /// <summary>
    /// Le cœur du 6a : un clic au CENTRE de la carte, loin de tout texte et de tout bouton, sur le
    /// milieu de la bande d'art. Si le bouton pleine surface avait un fond nul quelque part, ce
    /// pixel-là serait mort et ce test le dirait.
    /// </summary>
    [AvaloniaFact]
    public async Task ClickingTheMiddleOfAnInstanceCard_OpensItsDetailPage()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        var (window, shell) = await ShowPopulatedHomeAsync(provider, fileSystem);

        var card = CardOf(window);
        shell.CurrentPage.ShouldBeOfType<HomeViewModel>();

        Click(window, card, new Point(card.Bounds.Width / 2d, 48d));

        shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();

        window.Close();
    }

    /// <summary>
    /// Deuxième maillon : le « … » de l'en-tête du détail. Un flyout qui ne s'ouvre pas au clic est
    /// exactement le genre de panne qu'aucun test de ViewModel ne peut voir, puisque la commande
    /// n'existe pas — c'est le bouton lui-même qui porte le menu.
    /// </summary>
    [AvaloniaFact]
    public async Task ClickingTheMoreButtonOfTheDetailHeader_OpensItsFlyout()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        var (window, shell) = await ShowPopulatedHomeAsync(provider, fileSystem);

        var card = CardOf(window);
        Click(window, card, new Point(card.Bounds.Width / 2d, 48d));
        shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();

        var more = MoreButtonOf(window);
        Click(window, more, new Point(more.Bounds.Width / 2d, more.Bounds.Height / 2d));

        more.Flyout.ShouldNotBeNull().IsOpen.ShouldBeTrue();

        window.Close();
    }

    /// <summary>
    /// Troisième maillon : « Vérifier l'instance », première entrée du menu, ouvre le docteur.
    /// </summary>
    [AvaloniaFact]
    public async Task ClickingCheckInstanceInThatFlyout_ShowsTheDoctorDialog()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        var (window, shell) = await ShowPopulatedHomeAsync(provider, fileSystem);

        var card = CardOf(window);
        Click(window, card, new Point(card.Bounds.Width / 2d, 48d));
        shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();

        var more = MoreButtonOf(window);
        Click(window, more, new Point(more.Bounds.Width / 2d, more.Bounds.Height / 2d));

        var checkItem = window
            .GetVisualDescendants()
            .OfType<MenuItem>()
            .First(item => Equals(item.Header, Strings.Resolve("Instance_CheckInstance")));

        // Seul le clic déclenche le diagnostic : la commande n'est jamais invoquée à la main ici,
        // sinon le test passerait même sur une entrée de menu morte.
        Click(window, checkItem, new Point(checkItem.Bounds.Width / 2d, checkItem.Bounds.Height / 2d));
        SettleUntil(window, () => shell.Overlay.Active is InstanceDoctorDialogViewModel);

        shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();

        window.Close();
    }

    /// <summary>
    /// L'affordance demandée après coup : au repos la pastille « Ouvrir » est invisible, au survol
    /// de la carte elle apparaît. Le survol se conduit ici au pointeur, pas en posant une classe à
    /// la main : c'est bien le sélecteur descendant de Card.axaml qu'on veut prouver.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task HoveringACard_RevealsItsOpenHint(string variant)
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        var theme = variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var (window, _) = await ShowPopulatedHomeAsync(provider, fileSystem, theme);

        var card = CardOf(window);
        var hint = card.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("openHint"));

        hint.Opacity.ShouldBe(0d);

        Hover(window, card, new Point(card.Bounds.Width / 2d, 48d));
        SettleUntil(window, () => hint.Opacity >= 1d);

        hint.Opacity.ShouldBe(1d);
        hint.IsHitTestVisible.ShouldBeFalse();

        window.Close();
    }

    /// <summary>
    /// Le survol change aussi la vitre elle-même, et franchement : arête chaude et vitre montée
    /// d'un cran de profondeur. C'est ce qui manquait — l'ancien survol reprenait celui d'un champ
    /// de saisie et personne ne le voyait sur une carte.
    /// </summary>
    [AvaloniaFact]
    public async Task HoveringACard_LightsUpItsEdgeAndDeepensItsGlass()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        var (window, _) = await ShowPopulatedHomeAsync(provider, fileSystem);

        var card = CardOf(window);
        var frame = card.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card"));

        var restEdge = (frame.BorderBrush as ISolidColorBrush).ShouldNotBeNull().Color;
        var restGlass = (frame.Background as ISolidColorBrush).ShouldNotBeNull().Color;

        Hover(window, card, new Point(card.Bounds.Width / 2d, 48d));
        SettleUntil(window, () => (frame.BorderBrush as ISolidColorBrush)?.Color.A >= 0x99);

        var hotEdge = (frame.BorderBrush as ISolidColorBrush).ShouldNotBeNull().Color;
        var hotGlass = (frame.Background as ISolidColorBrush).ShouldNotBeNull().Color;

        hotEdge.A.ShouldBeGreaterThan(restEdge.A);
        hotGlass.A.ShouldBeGreaterThan(restGlass.A);

        window.Close();
    }

    /// <summary>
    /// Le survol ne déplace RIEN : ni la carte, ni son contenu. Le design system interdit la
    /// géométrie animée, et une carte qui glisse sous le curseur rend la grille instable. Seules la
    /// lumière et la profondeur changent.
    /// </summary>
    [AvaloniaFact]
    public async Task HoveringACard_MovesNoGeometryAtAll()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        var (window, _) = await ShowPopulatedHomeAsync(provider, fileSystem);

        var card = CardOf(window);
        var frame = card.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card"));
        var before = frame.Bounds;
        var beforeTransform = frame.RenderTransform;

        Hover(window, card, new Point(card.Bounds.Width / 2d, 48d));

        frame.Bounds.ShouldBe(before);
        frame.RenderTransform.ShouldBe(beforeTransform);

        window.Close();
    }

    /// <summary>
    /// Et pour qui ne survole jamais : « Ouvrir » ouvre le menu « … » de la carte, avant Renommer.
    /// </summary>
    [AvaloniaFact]
    public async Task TheCardMenu_LeadsWithOpen()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        var (window, shell) = await ShowPopulatedHomeAsync(provider, fileSystem);

        var card = CardOf(window);
        var actions = card
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Flyout is MenuFlyout);

        // Le menu s'ouvre au clic, pas à la lecture : tant qu'il n'est pas ouvert, ses entrées ne
        // sont pas dans l'arbre logique et leurs liaisons ne sont pas évaluées.
        Click(window, actions, new Point(actions.Bounds.Width / 2d, actions.Bounds.Height / 2d));

        var items = window
            .GetVisualDescendants()
            .OfType<MenuItem>()
            .Where(item => item.FindLogicalAncestorOfType<MenuFlyoutPresenter>() is not null)
            .ToArray();

        items[0].Header.ShouldBe(Strings.Resolve("Card_Open"));
        items[1].Header.ShouldBe(Strings.Resolve("Card_Rename"));

        Click(window, items[0], new Point(items[0].Bounds.Width / 2d, items[0].Bounds.Height / 2d));

        shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();

        window.Close();
    }

    private static Button MoreButtonOf(Window window)
        => window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Flyout is MenuFlyout menu
                && menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, Strings.Resolve("Instance_CheckInstance"))));
}

/// <summary>Accès aux textes statiques du dictionnaire de langue courant, pour les assertions.</summary>
internal static class Strings
{
    public static string Resolve(string key)
    {
        Application.Current!.TryFindResource(key, out var value).ShouldBeTrue($"clé {key} introuvable");

        return value.ShouldBeOfType<string>();
    }
}