using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Prospect.Desktop.Layout;

using Shouldly;

namespace Prospect.Desktop.Tests.Layout;

/// <summary>
/// La grille fluide du navigateur de mods, mesurée hors de tout écran : le compte de colonnes, le
/// partage de la largeur, la hauteur par rangée et les cas dégénérés.
/// </summary>
/// <remarks>
/// La garde de mise en page vérifie le résultat DANS l'application (voir
/// <c>ResponsiveRegressionTests.ModBrowser_Grid_AddsAColumnAsTheWindowGrows</c>). Ces tests-ci
/// isolent l'arithmétique, celle qui décide qu'à 688 points de large on rend deux colonnes de 336
/// et pas une seule colonne de 688.
/// </remarks>
public sealed class FluidGridPanelTests
{
    private static FluidGridPanel Layout(double width, int children, double minItemWidth = 320, double spacing = 16)
    {
        var panel = new FluidGridPanel { MinItemWidth = minItemWidth, ColumnSpacing = spacing, RowSpacing = spacing };
        for (var index = 0; index < children; index++)
        {
            panel.Children.Add(new Border { Height = 100 });
        }

        var window = new Window { Content = panel, Width = width, Height = 800 };
        window.Show();
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        window.Close();

        return panel;
    }

    [AvaloniaTheory]
    [InlineData(688, 2)] // Zone de contenu à la largeur plancher de la fenêtre (960 - sidebar - marges).
    [InlineData(828, 2)] // 1100 de large.
    [InlineData(1008, 3)] // 1280 de large, la taille de référence du design.
    [InlineData(1328, 4)]
    [InlineData(300, 1)] // Plus étroit qu'une carte : une colonne, jamais zéro.
    public void Measure_ColumnCount_FollowsTheAvailableWidth(double width, int expected)
        => Layout(width, children: 12).ColumnCount.ShouldBe(expected);

    [AvaloniaFact]
    public void Arrange_Columns_ShareTheWholeWidthRatherThanKeepingTheMinimum()
    {
        // Le défaut que cette grille corrige : des cartes à largeur figée laissent une bande vide
        // à droite de chaque rangée dès que la fenêtre ne tombe pas juste.
        var panel = Layout(1008, children: 6);

        panel.ColumnCount.ShouldBe(3);
        var itemWidth = panel.Children[0].Bounds.Width;

        // Un point de tolérance : l'alignement au pixel arrondit chaque largeur de colonne.
        itemWidth.ShouldBe((1008d - 32d) / 3d, tolerance: 1);
        ((itemWidth * 3) + 32).ShouldBe(1008, tolerance: 3);
    }

    [AvaloniaFact]
    public void Arrange_LaysChildrenOutRowByRow_WithoutOverlapping()
    {
        var panel = Layout(1008, children: 7);
        var boxes = panel.Children.Select(child => child.Bounds).ToArray();

        // Trois colonnes : la quatrième carte ouvre la deuxième rangée.
        boxes[3].X.ShouldBe(boxes[0].X, tolerance: 0.5);
        boxes[3].Y.ShouldBeGreaterThan(boxes[0].Bottom - 1);

        for (var left = 0; left < boxes.Length; left++)
        {
            for (var right = left + 1; right < boxes.Length; right++)
            {
                var intersection = boxes[left].Intersect(boxes[right]);
                (intersection.Width <= 1 || intersection.Height <= 1).ShouldBeTrue(
                    $"les cartes {left} et {right} se recouvrent sur {intersection}");
            }
        }
    }

    [AvaloniaFact]
    public void Measure_RowHeight_FollowsTheTallestCardOfThatRowAndNotOfTheWholeGrid()
    {
        // C'est ce qui disqualifiait une UniformGrid unique : elle aurait donné à TOUTES les
        // rangées la hauteur de la plus haute carte du lot, donc un trou sous chacune des autres.
        // MinHeight et non Height : une carte s'étire jusqu'à la hauteur de sa rangée, ce qui est
        // précisément ce qui garde les pieds de carte alignés d'une carte à l'autre.
        var panel = new FluidGridPanel { MinItemWidth = 320, ColumnSpacing = 16, RowSpacing = 16 };
        panel.Children.Add(new Border { MinHeight = 300 });
        panel.Children.Add(new Border { MinHeight = 100 });
        panel.Children.Add(new Border { MinHeight = 80 });
        panel.Children.Add(new Border { MinHeight = 80 });

        var window = new Window { Content = panel, Width = 700, Height = 800 };
        window.Show();
        panel.Measure(new Size(700, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 700, panel.DesiredSize.Height));

        panel.ColumnCount.ShouldBe(2);
        panel.Children[0].Bounds.Height.ShouldBe(300);
        panel.Children[1].Bounds.Height.ShouldBe(300); // Alignée sur sa voisine de rangée.
        panel.Children[2].Bounds.Height.ShouldBe(80); // Rangée suivante : sa propre hauteur, pas 300.
        panel.Children[2].Bounds.Y.ShouldBe(316); // 300 de rangée + 16 de gouttière.
        panel.DesiredSize.Height.ShouldBe(300 + 16 + 80);

        window.Close();
    }

    [AvaloniaFact]
    public void Measure_ASingleCard_KeepsAColumnWidthInsteadOfStretchingAcrossThePage()
    {
        // Une recherche à un seul résultat ne doit pas produire une carte large comme l'écran :
        // c'est le comportement d'un auto-fill CSS, et c'est celui qu'on veut.
        var panel = Layout(1008, children: 1);

        panel.ColumnCount.ShouldBe(3);
        panel.Children[0].Bounds.Width.ShouldBeLessThan(400);
    }

    [AvaloniaFact]
    public void Measure_NoChildren_TakesNoHeight()
        => Layout(1008, children: 0).DesiredSize.Height.ShouldBe(0);

    [AvaloniaFact]
    public void Measure_UnboundedWidth_FallsBackToOneColumnOfTheMinimumWidth()
    {
        // Mesure sous une contrainte infinie (le piège documenté sur HomeView) : sans repli, la
        // division donnerait un NaN qui contaminerait toute la mise en page.
        var panel = new FluidGridPanel { MinItemWidth = 320, ColumnSpacing = 16, RowSpacing = 16 };
        panel.Children.Add(new Border { Height = 100 });

        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        panel.ColumnCount.ShouldBe(1);
        panel.DesiredSize.Width.ShouldBe(320);
        panel.DesiredSize.Height.ShouldBe(100);
    }
}