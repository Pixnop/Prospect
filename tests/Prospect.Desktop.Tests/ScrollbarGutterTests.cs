using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Prospect.Desktop.Tests.Support;

using Shouldly;

namespace Prospect.Desktop.Tests;

/// <summary>
/// La poignée de défilement doit rester attrapable. Sur une fenêtre à zone client étendue, Windows
/// garde les derniers points INTÉRIEURS pour le redimensionnement du cadre : une barre collée au
/// bord se faisait voler son pouce, et on saisissait la fenêtre au lieu de la piste. La correction
/// est une gouttière posée sur tous les ScrollViewer (Styles/Controls/ScrollViewer.axaml).
///
/// Ces tests mesurent la gouttière ; la sensation au doigt, elle, ne se vérifie que sur Windows.
/// </summary>
public sealed class ScrollbarGutterTests
{
    /// <summary>
    /// Largeur de la bande de redimensionnement conservée par Windows à l'intérieur d'une fenêtre
    /// à zone client étendue : SM_CXSIZEFRAME + SM_CXPADDEDBORDER, soit 8 points, indépendamment du
    /// facteur d'échelle (AdjustWindowRectExForDpi rend des pixels physiques que le même facteur
    /// reconvertit). La gouttière doit être strictement plus grande, sinon elle ne sert à rien.
    /// </summary>
    private const double WindowsResizeBandThickness = 8d;

    private static ScrollViewer ShowScrollViewer(out Window window, bool flush = false)
    {
        var scroller = new ScrollViewer
        {
            Content = new Border { Height = 4000, Width = 4000 },
        };

        if (flush)
        {
            scroller.Classes.Add("flush");
        }

        window = new Window { Content = scroller, Width = 400, Height = 300 };
        window.Show();
        window.Settle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        return scroller;
    }

    private static ScrollBar ScrollBarOf(ScrollViewer scroller, Orientation orientation)
        => scroller
            .GetVisualDescendants()
            .OfType<ScrollBar>()
            .First(bar => bar.Orientation == orientation);

    [AvaloniaFact]
    public void VerticalScrollBar_KeepsAGutterWiderThanTheWindowsResizeBand()
    {
        var scroller = ShowScrollViewer(out _);
        var bar = ScrollBarOf(scroller, Orientation.Vertical);

        var barRight = bar.TranslatePoint(new Point(bar.Bounds.Width, 0), scroller).ShouldNotBeNull().X;
        var gutter = scroller.Bounds.Width - barRight;

        gutter.ShouldBeGreaterThan(WindowsResizeBandThickness);
    }

    [AvaloniaFact]
    public void HorizontalScrollBar_KeepsTheSameGutterAgainstTheBottomEdge()
    {
        var scroller = ShowScrollViewer(out _);
        var bar = ScrollBarOf(scroller, Orientation.Horizontal);

        var barBottom = bar.TranslatePoint(new Point(0, bar.Bounds.Height), scroller).ShouldNotBeNull().Y;
        var gutter = scroller.Bounds.Height - barBottom;

        gutter.ShouldBeGreaterThan(WindowsResizeBandThickness);
    }

    /// <summary>
    /// Le jeton et la valeur littérale écrite dans la feuille de style ne peuvent pas diverger :
    /// Avalonia ne sait pas composer un seul axe d'un Thickness à partir d'une ressource, donc la
    /// feuille écrit le nombre à la main et c'est ce test qui les tient ensemble.
    /// </summary>
    [AvaloniaFact]
    public void TheGutterApplied_IsExactlyTheScrollbarGutterToken()
    {
        Application.Current!.TryFindResource("ScrollbarGutter", out var token).ShouldBeTrue();
        var expected = token.ShouldBeOfType<double>();

        var scroller = ShowScrollViewer(out _);
        ScrollBarOf(scroller, Orientation.Vertical).Margin.Right.ShouldBe(expected);
        ScrollBarOf(scroller, Orientation.Horizontal).Margin.Bottom.ShouldBe(expected);
    }

    /// <summary>
    /// La largeur de la piste reste celle du design : la gouttière écarte la barre, elle ne
    /// l'épaissit pas.
    /// </summary>
    [AvaloniaFact]
    public void TheTrackKeepsItsDesignWidth()
    {
        Application.Current!.TryFindResource("ScrollbarW", out var token).ShouldBeTrue();
        var expected = token.ShouldBeOfType<double>();

        var scroller = ShowScrollViewer(out _);
        ScrollBarOf(scroller, Orientation.Vertical).Bounds.Width.ShouldBe(expected);
    }

    /// <summary>
    /// Une surface qui ne touche jamais le bord de la fenêtre — la liste déroulante d'un ComboBox —
    /// se désinscrit par la classe `flush` : la gouttière n'y achèterait que du vide.
    /// </summary>
    [AvaloniaFact]
    public void AFlushScrollViewer_HasNoGutterAtAll()
    {
        var scroller = ShowScrollViewer(out _, flush: true);
        ScrollBarOf(scroller, Orientation.Vertical).Margin.Right.ShouldBe(0d);
        ScrollBarOf(scroller, Orientation.Horizontal).Margin.Bottom.ShouldBe(0d);
    }

    /// <summary>
    /// La gouttière ne mange pas la zone lisible : dans le gabarit Fluent le contenu occupe toute
    /// la grille et les barres flottent au-dessus. Le presenter garde donc la largeur du
    /// ScrollViewer, gouttière ou pas.
    /// </summary>
    [AvaloniaFact]
    public void TheGutter_DoesNotNarrowTheScrollableViewport()
    {
        var scroller = ShowScrollViewer(out _);
        scroller.Viewport.Width.ShouldBe(scroller.Bounds.Width);
    }
}