using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

using Prospect.Desktop.Layout;
using Prospect.Desktop.Tests.Support;

using Shouldly;

namespace Prospect.Desktop.Tests;

/// <summary>
/// L'arpenteur vérifié sur des cas fabriqués. Une garde de non-régression qui ne trouve rien est
/// indiscernable d'une garde cassée : ces tests montent des fenêtres qui violent délibérément
/// chaque invariant, et exigent que l'arpenteur les nomme — puis la version corrigée du même cas,
/// et exigent qu'il se taise. Sans ça, la suite de <see cref="ResponsiveRegressionTests"/> ne
/// prouverait rien le jour où une modification de l'arpenteur l'aveuglerait.
/// </summary>
public sealed class LayoutInvariantWalkerTests
{
    private const string LongText = "Un libellé beaucoup trop long pour la place qu'on lui laisse";

    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 400, Height = 200 };
        window.Show();
        window.Settle();

        return window;
    }

    private static Border Box(double width, double height, double left = 0)
        => new()
        {
            Width = width,
            Height = height,
            Margin = new Avalonia.Thickness(left, 0, 0, 0),
            Background = Brushes.Gray,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };

    [AvaloniaFact]
    public void ALayoutThatFits_ReportsNothing()
    {
        var window = Show(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Court" },
                Box(100, 40),
            },
        });

        LayoutInvariantWalker.Inspect(window).ShouldBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public void TextWiderThanItsPlace_WithoutTrimmingOrWrapping_IsReported()
    {
        // Le texte est mesuré sous une contrainte de 80px : sa taille désirée, bornée par Avalonia
        // à la place disponible, a l'air de tenir. Seule la mise en page du texte dit la vérité,
        // et c'est elle que l'arpenteur regarde.
        var window = Show(new Border { Width = 80, Child = new TextBlock { Text = LongText } });

        var violation = LayoutInvariantWalker.Inspect(window).ShouldHaveSingleItem();

        violation.Invariant.ShouldBe(LayoutInvariant.TextOverflow);
        violation.Path.ShouldContain("TextBlock");
        violation.Detail.ShouldContain("sans TextTrimming ni TextWrapping");

        window.Close();
    }

    [AvaloniaFact]
    public void TheSameTextWithTrimming_IsAccepted()
    {
        var window = Show(new Border
        {
            Width = 80,
            Child = new TextBlock { Text = LongText, TextTrimming = TextTrimming.CharacterEllipsis },
        });

        LayoutInvariantWalker.Inspect(window).ShouldBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public void AChildPaintedOutsideItsParentBox_IsReported()
    {
        // Le défaut signalé en test réel, réduit à sa plus simple expression : un panneau horizontal
        // mesure ses enfants sans contrainte de largeur, puis les arrange à la queue leu leu. Le
        // panneau, lui, ne reçoit que la largeur de sa colonne — son contenu sort donc de sa boîte
        // et va se peindre sur le voisin, sans qu'aucune taille désirée n'ait l'air anormale.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*") };
        var overflowing = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        overflowing.Children.Add(Box(300, 40));
        grid.Children.Add(overflowing);

        var window = Show(grid);

        var violation = LayoutInvariantWalker.Inspect(window)
            .ShouldHaveSingleItem();

        violation.Invariant.ShouldBe(LayoutInvariant.Containment);
        violation.Detail.ShouldContain("déborde de la boîte de son parent");

        window.Close();
    }

    [AvaloniaFact]
    public void OverlappingSiblings_AreReported()
    {
        var grid = new Grid();
        grid.Children.Add(Box(200, 60));
        grid.Children.Add(Box(200, 60, left: 100));

        var window = Show(grid);

        var violation = LayoutInvariantWalker.Inspect(window).ShouldHaveSingleItem();

        violation.Invariant.ShouldBe(LayoutInvariant.Overlap);
        violation.Detail.ShouldContain("recouvre");

        window.Close();
    }

    [AvaloniaFact]
    public void AnOverlapDeclaredOnTheChild_IsAccepted()
    {
        var grid = new Grid();
        grid.Children.Add(Box(200, 60));
        var badge = Box(200, 60, left: 100);
        LayoutContract.SetAllowsOverlap(badge, true);
        grid.Children.Add(badge);

        var window = Show(grid);

        LayoutInvariantWalker.Inspect(window).ShouldBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public void AnOverlapDeclaredOnTheContainer_IsAccepted()
    {
        var panel = new Panel();
        panel.Children.Add(Box(200, 60));
        panel.Children.Add(Box(200, 60, left: 100));
        LayoutContract.SetAllowsOverlap(panel, true);

        var window = Show(panel);

        LayoutInvariantWalker.Inspect(window).ShouldBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public void ContentScrolledOutOfItsViewport_IsNotMistakenForAnOverflow()
    {
        // Un ScrollViewer n'est pas un débordement : son contenu est jugé sur ce que le rognage en
        // laisse voir, sinon toute page défilable serait signalée en permanence.
        var window = Show(new ScrollViewer
        {
            Height = 60,
            Content = new StackPanel
            {
                Children = { Box(100, 400) },
            },
        });

        LayoutInvariantWalker.Inspect(window).ShouldBeEmpty();

        window.Close();
    }
}