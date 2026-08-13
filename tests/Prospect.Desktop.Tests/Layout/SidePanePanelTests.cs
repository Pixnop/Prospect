using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Prospect.Desktop.Layout;

using Shouldly;

namespace Prospect.Desktop.Tests.Layout;

/// <summary>
/// Deux volets côte à côte tant que la largeur le permet, empilés sinon — et empilés dans un ordre
/// qui compte : tête, volet latéral, corps long. Un simple retour à la ligne mettrait les
/// dépendances à cocher sous des notes de version longues de trois écrans.
/// </summary>
public sealed class SidePanePanelTests
{
    private static Border Block(string name, double height)
        => new() { Name = name, Height = height, MinWidth = 1 };

    private static SidePanePanel Build(double width, out Border head, out Border side, out Border body)
    {
        head = Block("head", 40);
        side = Block("side", 120);
        body = Block("body", 300);

        SidePanePanel.SetRole(head, PaneRole.Head);
        SidePanePanel.SetRole(side, PaneRole.Side);
        SidePanePanel.SetRole(body, PaneRole.Body);

        var panel = new SidePanePanel { SideWidth = 200, Spacing = 10, SideBySideThreshold = 500 };
        panel.Children.Add(head);
        panel.Children.Add(side);
        panel.Children.Add(body);

        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));

        return panel;
    }

    [AvaloniaFact]
    public void WideEnough_PutsTheSidePaneOnTheRightAndTheBodyUnderTheHead()
    {
        var panel = Build(700, out var head, out var side, out var body);

        panel.IsSideBySide.ShouldBeTrue();

        // Colonne principale : 700 - 10 de gouttière - 200 de volet = 490.
        head.Bounds.Left.ShouldBe(0d);
        head.Bounds.Width.ShouldBe(490d);
        body.Bounds.Left.ShouldBe(0d);
        body.Bounds.Top.ShouldBe(50d);

        side.Bounds.Left.ShouldBe(500d);
        side.Bounds.Width.ShouldBe(200d);
        side.Bounds.Top.ShouldBe(0d);
    }

    /// <summary>
    /// Le cœur du panneau : une fois empilé, le volet latéral passe AVANT le corps long. C'est ce
    /// qui garde les cases à cocher au-dessus des notes de version, pas en dessous.
    /// </summary>
    [AvaloniaFact]
    public void TooNarrow_StacksTheSidePaneAboveTheLongBody()
    {
        var panel = Build(420, out var head, out var side, out var body);

        panel.IsSideBySide.ShouldBeFalse();

        head.Bounds.Top.ShouldBe(0d);
        side.Bounds.Top.ShouldBe(50d);
        body.Bounds.Top.ShouldBe(180d);

        head.Bounds.Width.ShouldBe(420d);
        side.Bounds.Width.ShouldBe(420d);
        body.Bounds.Width.ShouldBe(420d);
    }

    [AvaloniaFact]
    public void TheHeightIsTheTallestColumn_NotTheirSum()
    {
        // Principale : 40 + 10 + 300 = 350. Latérale : 120. La plus haute gagne.
        var panel = Build(700, out _, out _, out _);

        panel.DesiredSize.Height.ShouldBe(350d);
    }

    /// <summary>
    /// Un enfant masqué ne laisse pas de blanc derrière lui : sans quoi un plan sans dépendances
    /// garderait une gouttière là où la liste aurait été.
    /// </summary>
    [AvaloniaFact]
    public void AHiddenChild_TakesNeitherSpaceNorGutter()
    {
        var head = Block("head", 40);
        var side = Block("side", 0);
        var body = Block("body", 100);
        side.IsVisible = false;

        SidePanePanel.SetRole(head, PaneRole.Head);
        SidePanePanel.SetRole(side, PaneRole.Side);
        SidePanePanel.SetRole(body, PaneRole.Body);

        var panel = new SidePanePanel { SideWidth = 200, Spacing = 10, SideBySideThreshold = 500 };
        panel.Children.Add(head);
        panel.Children.Add(side);
        panel.Children.Add(body);

        panel.Measure(new Size(420, double.PositiveInfinity));

        panel.DesiredSize.Height.ShouldBe(150d);
    }

    [AvaloniaFact]
    public void WithoutAnySidePane_ItNeverSplits()
    {
        var head = Block("head", 40);
        SidePanePanel.SetRole(head, PaneRole.Head);

        var panel = new SidePanePanel { SideWidth = 200, Spacing = 10, SideBySideThreshold = 500 };
        panel.Children.Add(head);

        panel.Measure(new Size(900, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 900, panel.DesiredSize.Height));

        panel.IsSideBySide.ShouldBeFalse();
        head.Bounds.Width.ShouldBe(900d);
    }

    [AvaloniaFact]
    public void Role_NullArguments_AreRejected()
    {
        Should.Throw<ArgumentNullException>(() => SidePanePanel.GetRole(null!));
        Should.Throw<ArgumentNullException>(() => SidePanePanel.SetRole(null!, PaneRole.Side));
    }
}