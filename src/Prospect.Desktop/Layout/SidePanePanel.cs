using Avalonia;
using Avalonia.Controls;

namespace Prospect.Desktop.Layout;

/// <summary>Rôle d'un enfant de <see cref="SidePanePanel"/>.</summary>
public enum PaneRole
{
    /// <summary>Tête du volet principal : ce qui doit rester en haut dans les deux dispositions.</summary>
    Head,

    /// <summary>Volet latéral : à droite quand la place le permet, juste sous la tête sinon.</summary>
    Side,

    /// <summary>Corps long du volet principal : sous la tête à gauche, tout en bas une fois empilé.</summary>
    Body,
}

/// <summary>
/// Deux volets côte à côte tant que la largeur le permet, empilés sinon — et empilés dans l'ordre
/// qui garde le volet latéral VISIBLE : tête, volet latéral, puis le corps long.
/// </summary>
/// <remarks>
/// <para>
/// Écrit parce qu'Avalonia 11.3 n'a aucun moyen de faire basculer une <see cref="Grid"/> de deux
/// colonnes à une seule selon la largeur : il n'existe ni requête de conteneur, ni sélecteur de
/// style sur une dimension. Le seul endroit où la largeur disponible est connue est la mesure,
/// donc c'est un panneau.
/// </para>
/// <para>
/// L'ordre d'empilement est la raison d'être de ce panneau, et pas un détail. Un simple retour à la
/// ligne mettrait le volet latéral APRÈS le corps long, c'est-à-dire, dans le dialogue
/// d'installation, les dépendances à cocher sous des notes de version qui peuvent faire trois
/// écrans. Ce qui demande une décision doit rester au-dessus de ce qui se lit.
/// </para>
/// </remarks>
public sealed class SidePanePanel : Panel
{
    /// <summary>Rôle de l'enfant : tête, volet latéral, ou corps long. <see cref="PaneRole.Head"/> par défaut.</summary>
    public static readonly AttachedProperty<PaneRole> RoleProperty =
        AvaloniaProperty.RegisterAttached<SidePanePanel, Control, PaneRole>("Role");

    /// <summary>Largeur du volet latéral quand les deux volets sont côte à côte.</summary>
    public static readonly StyledProperty<double> SideWidthProperty =
        AvaloniaProperty.Register<SidePanePanel, double>(nameof(SideWidth), 280d);

    /// <summary>Gouttière entre les deux volets, et entre deux blocs empilés.</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<SidePanePanel, double>(nameof(Spacing), 16d);

    /// <summary>
    /// Largeur totale en dessous de laquelle on empile. Doit laisser au volet principal de quoi
    /// respirer une fois le volet latéral et la gouttière retirés, sans quoi les deux colonnes
    /// existent mais aucune n'est lisible.
    /// </summary>
    public static readonly StyledProperty<double> SideBySideThresholdProperty =
        AvaloniaProperty.Register<SidePanePanel, double>(nameof(SideBySideThreshold), 600d);

    private static readonly DirectProperty<SidePanePanel, bool> IsSideBySideProperty =
        AvaloniaProperty.RegisterDirect<SidePanePanel, bool>(nameof(IsSideBySide), panel => panel.IsSideBySide);

    private bool _isSideBySide;

    static SidePanePanel()
        => AffectsMeasure<SidePanePanel>(SideWidthProperty, SpacingProperty, SideBySideThresholdProperty);

    /// <inheritdoc cref="SideWidthProperty" />
    public double SideWidth
    {
        get => GetValue(SideWidthProperty);
        set => SetValue(SideWidthProperty, value);
    }

    /// <inheritdoc cref="SpacingProperty" />
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <inheritdoc cref="SideBySideThresholdProperty" />
    public double SideBySideThreshold
    {
        get => GetValue(SideBySideThresholdProperty);
        set => SetValue(SideBySideThresholdProperty, value);
    }

    /// <summary>Disposition de la dernière mesure. Exposé pour la garde de mise en page.</summary>
    public bool IsSideBySide
    {
        get => _isSideBySide;
        private set => SetAndRaise(IsSideBySideProperty, ref _isSideBySide, value);
    }

    /// <summary>Lit le rôle d'un enfant.</summary>
    public static PaneRole GetRole(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return control.GetValue(RoleProperty);
    }

    /// <summary>Pose le rôle d'un enfant.</summary>
    public static void SetRole(Control control, PaneRole value)
    {
        ArgumentNullException.ThrowIfNull(control);

        control.SetValue(RoleProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var sideBySide = ResolveSideBySide(availableSize.Width);
        IsSideBySide = sideBySide;

        return sideBySide ? MeasureSideBySide(availableSize) : MeasureStacked(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var sideBySide = ResolveSideBySide(finalSize.Width);
        IsSideBySide = sideBySide;

        if (sideBySide)
        {
            ArrangeSideBySide(finalSize);
        }
        else
        {
            ArrangeStacked(finalSize);
        }

        return finalSize;
    }

    private Size MeasureSideBySide(Size availableSize)
    {
        var (mainWidth, sideWidth) = ResolveColumns(availableSize.Width);

        var mainCursor = 0d;
        var sideCursor = 0d;

        foreach (var child in Children)
        {
            var isSide = GetRole(child) == PaneRole.Side;
            child.Measure(new Size(isSide ? sideWidth : mainWidth, double.PositiveInfinity));

            if (isSide)
            {
                Advance(ref sideCursor, child.DesiredSize.Height);
            }
            else
            {
                Advance(ref mainCursor, child.DesiredSize.Height);
            }
        }

        var width = double.IsInfinity(availableSize.Width) ? mainWidth + Spacing + sideWidth : availableSize.Width;

        return new Size(width, Math.Max(Consumed(mainCursor), Consumed(sideCursor)));
    }

    private Size MeasureStacked(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? SideWidth : availableSize.Width;
        var cursor = 0d;

        foreach (var child in InStackedOrder())
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            Advance(ref cursor, child.DesiredSize.Height);
        }

        return new Size(width, Consumed(cursor));
    }

    private void ArrangeSideBySide(Size finalSize)
    {
        var (mainWidth, sideWidth) = ResolveColumns(finalSize.Width);
        var mainCursor = 0d;
        var sideCursor = 0d;

        foreach (var child in Children)
        {
            var height = child.DesiredSize.Height;
            if (height <= 0d)
            {
                continue;
            }

            if (GetRole(child) == PaneRole.Side)
            {
                child.Arrange(new Rect(mainWidth + Spacing, sideCursor, sideWidth, height));
                Advance(ref sideCursor, height);
            }
            else
            {
                child.Arrange(new Rect(0d, mainCursor, mainWidth, height));
                Advance(ref mainCursor, height);
            }
        }
    }

    private void ArrangeStacked(Size finalSize)
    {
        var cursor = 0d;
        foreach (var child in InStackedOrder())
        {
            var height = child.DesiredSize.Height;
            if (height <= 0d)
            {
                continue;
            }

            child.Arrange(new Rect(0d, cursor, finalSize.Width, height));
            Advance(ref cursor, height);
        }
    }

    // Tête, volet latéral, corps long : ce qui décide passe avant ce qui se lit.
    private IEnumerable<Control> InStackedOrder()
        => Children.Where(child => GetRole(child) == PaneRole.Head)
            .Concat(Children.Where(child => GetRole(child) == PaneRole.Side))
            .Concat(Children.Where(child => GetRole(child) == PaneRole.Body));

    // Le curseur porte la gouttière du bloc SUIVANT, pas celle du bloc posé : c'est ce qui permet à
    // un enfant masqué (hauteur nulle) de ne laisser ni place ni gouttière derrière lui, et à
    // Consumed de retrancher la dernière, celle qui ne sépare rien.
    private void Advance(ref double cursor, double height)
    {
        if (height > 0d)
        {
            cursor += height + Spacing;
        }
    }

    private double Consumed(double cursor) => Math.Max(0d, cursor - Spacing);

    private bool ResolveSideBySide(double availableWidth)
    {
        if (double.IsInfinity(availableWidth) || double.IsNaN(availableWidth))
        {
            return true;
        }

        return availableWidth >= SideBySideThreshold && Children.Any(child => GetRole(child) == PaneRole.Side);
    }

    private (double Main, double Side) ResolveColumns(double availableWidth)
    {
        if (double.IsInfinity(availableWidth) || double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return (Math.Max(1d, SideBySideThreshold - SideWidth - Spacing), Math.Max(1d, SideWidth));
        }

        var side = Math.Clamp(SideWidth, 1d, Math.Max(1d, availableWidth - Spacing - 1d));

        return (Math.Max(1d, availableWidth - Spacing - side), side);
    }
}