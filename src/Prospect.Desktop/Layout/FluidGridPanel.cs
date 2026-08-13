using Avalonia;
using Avalonia.Controls;

namespace Prospect.Desktop.Layout;

/// <summary>
/// Grille fluide : le nombre de colonnes se déduit de la largeur disponible, les colonnes se
/// partagent ensuite cette largeur à parts égales, et chaque RANGÉE prend la hauteur de son plus
/// grand enfant. C'est le comportement de <c>grid-template-columns: repeat(auto-fill,
/// minmax(&lt;MinItemWidth&gt;, 1fr))</c>, qui n'a pas d'équivalent dans Avalonia 11.3.
/// </summary>
/// <remarks>
/// <para>
/// Aucun panneau fourni ne fait ça, et chacun échoue pour une raison différente. Un
/// <see cref="WrapPanel"/> n'étire pas ses enfants : ils gardent la largeur qu'ils demandent, donc
/// il faut la figer dans la vue, ce qui laisse une bande vide à droite de chaque rangée dès que la
/// fenêtre ne tombe pas juste. Une <c>UniformGrid</c> unique donne à TOUTES ses rangées la hauteur
/// de la plus haute carte du lot, donc une seule description longue creuse un trou sous chacune
/// des autres. Une <c>UniformGrid</c> par rangée demanderait au ViewModel de découper ses
/// résultats en rangées, c'est-à-dire de connaître la largeur de la fenêtre.
/// </para>
/// <para>
/// Ce panneau ne virtualise PAS, et c'est volontaire. Le navigateur de mods ne lui confie qu'une
/// FENÊTRE de cartes (30, étendue par tranches, voir <c>ModBrowserViewModel</c>) : la paresse est
/// déjà en amont, là où elle compte, puisque c'est la construction des ViewModels qui déclenche
/// les téléchargements et les décodages de logos. Mesurer et arranger quelques dizaines d'enfants
/// est du travail linéaire trivial ; virtualiser ici ajouterait une dépendance
/// (<c>ItemsRepeater</c> vit dans un paquet à part) sans rien retirer au coût réel.
/// </para>
/// </remarks>
public sealed class FluidGridPanel : Panel
{
    /// <summary>
    /// Largeur minimale d'une carte, en points. C'est un SEUIL d'ajout de colonne, pas une largeur
    /// imposée : une fois le nombre de colonnes fixé, elles se partagent toute la place, donc une
    /// carte est toujours au moins aussi large que cette valeur.
    /// </summary>
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<FluidGridPanel, double>(nameof(MinItemWidth), 320d);

    /// <summary>Gouttière horizontale entre deux colonnes.</summary>
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<FluidGridPanel, double>(nameof(ColumnSpacing), 16d);

    /// <summary>Gouttière verticale entre deux rangées.</summary>
    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<FluidGridPanel, double>(nameof(RowSpacing), 16d);

    private static readonly DirectProperty<FluidGridPanel, int> ColumnCountProperty =
        AvaloniaProperty.RegisterDirect<FluidGridPanel, int>(nameof(ColumnCount), panel => panel.ColumnCount);

    private int _columnCount = 1;

    static FluidGridPanel()
        => AffectsMeasure<FluidGridPanel>(MinItemWidthProperty, ColumnSpacingProperty, RowSpacingProperty);

    /// <inheritdoc cref="MinItemWidthProperty" />
    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    /// <inheritdoc cref="ColumnSpacingProperty" />
    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    /// <inheritdoc cref="RowSpacingProperty" />
    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>
    /// Nombre de colonnes de la dernière mesure. Exposé pour la garde de mise en page, qui vérifie
    /// que la grille rend bien deux colonnes au plancher de la fenêtre, trois à la taille de
    /// référence du design et quatre au-delà.
    /// </summary>
    public int ColumnCount
    {
        get => _columnCount;
        private set => SetAndRaise(ColumnCountProperty, ref _columnCount, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = ResolveColumnCount(availableSize.Width);
        ColumnCount = columns;

        var itemWidth = ResolveItemWidth(availableSize.Width, columns);
        var childConstraint = new Size(itemWidth, double.PositiveInfinity);

        var height = 0d;
        for (var start = 0; start < Children.Count; start += columns)
        {
            var rowHeight = 0d;
            for (var offset = 0; offset < columns && start + offset < Children.Count; offset++)
            {
                var child = Children[start + offset];
                child.Measure(childConstraint);
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }

            height += rowHeight + RowSpacing;
        }

        // La gouttière sépare deux rangées, elle ne borde pas la grille : celle posée après la
        // dernière rangée est retirée.
        height = Math.Max(0d, height - RowSpacing);

        // Une largeur infinie (mesure sous un ScrollViewer horizontal) ne peut pas se traduire en
        // largeur désirée : on rend alors la place que les colonnes occupent réellement.
        var width = double.IsInfinity(availableSize.Width)
            ? (itemWidth * columns) + (ColumnSpacing * (columns - 1))
            : availableSize.Width;

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ResolveColumnCount(finalSize.Width);
        ColumnCount = columns;

        var itemWidth = ResolveItemWidth(finalSize.Width, columns);
        var top = 0d;

        for (var start = 0; start < Children.Count; start += columns)
        {
            var count = Math.Min(columns, Children.Count - start);

            // Toutes les cartes d'une rangée sont arrangées à la hauteur de la plus haute : sans
            // ça, un pied de carte « collé au bas » se retrouverait à une altitude différente
            // d'une carte à l'autre, ce qui est exactement le désordre à corriger.
            var rowHeight = 0d;
            for (var offset = 0; offset < count; offset++)
            {
                rowHeight = Math.Max(rowHeight, Children[start + offset].DesiredSize.Height);
            }

            for (var offset = 0; offset < count; offset++)
            {
                Children[start + offset].Arrange(new Rect(offset * (itemWidth + ColumnSpacing), top, itemWidth, rowHeight));
            }

            top += rowHeight + RowSpacing;
        }

        return finalSize;
    }

    private int ResolveColumnCount(double availableWidth)
    {
        if (double.IsInfinity(availableWidth) || double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return 1;
        }

        var minimum = Math.Max(1d, MinItemWidth);
        var spacing = Math.Max(0d, ColumnSpacing);

        // Une colonne de plus tient si elle apporte sa largeur minimale ET sa gouttière : ajouter
        // la gouttière des deux côtés de la division est le même calcul qu'un auto-fill CSS.
        // Le compte ne dépend JAMAIS du nombre d'enfants : une recherche à un seul résultat rend
        // une carte de largeur normale dans la première colonne, pas une carte étalée sur toute
        // la page.
        return Math.Max(1, (int)Math.Floor((availableWidth + spacing) / (minimum + spacing)));
    }

    private double ResolveItemWidth(double availableWidth, int columns)
    {
        if (double.IsInfinity(availableWidth) || double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return Math.Max(1d, MinItemWidth);
        }

        var spacing = Math.Max(0d, ColumnSpacing) * (columns - 1);

        return Math.Max(1d, (availableWidth - spacing) / columns);
    }
}