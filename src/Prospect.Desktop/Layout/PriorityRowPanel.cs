using Avalonia;
using Avalonia.Controls;

namespace Prospect.Desktop.Layout;

/// <summary>
/// Rangée horizontale à PRIORITÉ : les enfants sont posés de gauche à droite dans l'ordre déclaré,
/// et le premier qui ne tiendrait pas ENTIER sort du champ, lui et tous ceux qui le suivent. Aucun
/// enfant n'est jamais rendu à moitié.
/// </summary>
/// <remarks>
/// <para>
/// Le défaut qui a mené ici : la rangée de pastilles d'une carte du navigateur de mods était un
/// <see cref="ScrollViewer"/> à barre masquée. Une carte déjà installée porte trois pastilles
/// (« Installé · 2.0.0-pre.8 », la version de jeu, « client et serveur ») pour environ 330 points,
/// alors que la carte n'en offre que 200 à 300 : la dernière était donc COUPÉE EN PLEIN MOT contre
/// le bouton « Gérer », rendue « cli » ou « client et ». Un glyphe tranché ne se lit pas comme un
/// choix de mise en page, il se lit comme un défaut d'affichage.
/// </para>
/// <para>
/// La granularité est donc la PASTILLE ENTIÈRE, jamais le pixel. Ce panneau ne défile pas et ne
/// tronque pas : il décide, à la mesure, combien d'enfants tiennent en entier, et arrange les
/// autres hors du champ. Le rognage sur les bornes (<see cref="Visual.ClipToBounds"/>, posé par le
/// constructeur) fait le reste : ce qui est arrangé au-delà de la largeur finale n'est simplement
/// pas peint. Les enfants écartés gardent leur taille NATURELLE plutôt qu'une taille nulle, ce qui
/// n'a l'air d'un détail que jusqu'à la première mesure : un texte arrangé plus étroit que sa
/// propre mise en page est un débordement de texte, et c'est précisément ce que la garde de mise en
/// page interdit.
/// </para>
/// <para>
/// L'ordre de déclaration est donc l'ordre de priorité, et c'est une décision produit plutôt qu'une
/// mise en page : sur une carte de mod, « Installé · version » d'abord (le fait que l'utilisateur
/// venait chercher, celui qui explique que le bouton dise « Gérer »), la version de jeu ensuite, le
/// côté client/serveur en dernier, là où l'onglet Mods de l'instance le donne de toute façon.
/// </para>
/// <para>
/// L'écart est SIGNALÉ plutôt que silencieux : l'enfant marqué
/// <see cref="IsOverflowIndicatorProperty"/> est rendu, à la toute fin de la rangée, quand et
/// seulement quand au moins un enfant a été écarté. Sa place est réservée dès la décision, sinon il
/// serait lui-même le premier à ne pas tenir.
/// </para>
/// </remarks>
public sealed class PriorityRowPanel : Panel
{
    /// <summary>Tolérance de comparaison des largeurs, en points : l'épaisseur d'une erreur d'arrondi.</summary>
    private const double Epsilon = 0.5d;

    /// <summary>Gouttière entre deux enfants.</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<PriorityRowPanel, double>(nameof(Spacing), 6d);

    /// <summary>
    /// Marque l'enfant qui sert d'indicateur de débordement. Au plus un par rangée ; il ne
    /// participe jamais au flux normal et n'apparaît que si quelque chose a été écarté.
    /// </summary>
    public static readonly AttachedProperty<bool> IsOverflowIndicatorProperty =
        AvaloniaProperty.RegisterAttached<PriorityRowPanel, Control, bool>("IsOverflowIndicator");

    private static readonly DirectProperty<PriorityRowPanel, int> DroppedCountProperty =
        AvaloniaProperty.RegisterDirect<PriorityRowPanel, int>(nameof(DroppedCount), panel => panel.DroppedCount);

    private int _droppedCount;

    /// <summary>Construit la rangée. Le rognage sur les bornes est la moitié du contrat, il n'est pas optionnel.</summary>
    public PriorityRowPanel() => ClipToBounds = true;

    static PriorityRowPanel() => AffectsMeasure<PriorityRowPanel>(SpacingProperty);

    /// <inheritdoc cref="SpacingProperty" />
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Nombre d'enfants écartés à la dernière mesure. Exposé pour la garde de mise en page, qui
    /// vérifie qu'une rangée trop étroite écarte bien des pastilles entières plutôt que d'en couper
    /// une.
    /// </summary>
    public int DroppedCount
    {
        get => _droppedCount;
        private set => SetAndRaise(DroppedCountProperty, ref _droppedCount, value);
    }

    /// <summary>Lit le marqueur d'indicateur de débordement.</summary>
    /// <param name="control">Enfant de la rangée.</param>
    public static bool GetIsOverflowIndicator(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return control.GetValue(IsOverflowIndicatorProperty);
    }

    /// <summary>Pose le marqueur d'indicateur de débordement.</summary>
    /// <param name="control">Enfant de la rangée.</param>
    /// <param name="value">Vrai pour l'enfant qui signale l'écart.</param>
    public static void SetIsOverflowIndicator(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);

        control.SetValue(IsOverflowIndicatorProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var plan = Plan(availableSize.Width);
        DroppedCount = plan.Dropped;

        return new Size(plan.Width, plan.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var spacing = Math.Max(0d, Spacing);
        var plan = Plan(finalSize.Width);
        DroppedCount = plan.Dropped;

        var x = 0d;
        var placed = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            var indicator = GetIsOverflowIndicator(child);
            var keep = indicator ? plan.Dropped > 0 : placed < plan.Kept;
            if (!indicator)
            {
                placed++;
            }

            // Toujours la taille NATURELLE, jamais une taille rabotée : un enfant arrangé plus
            // étroit ou plus bas que sa propre mise en page est un débordement de contenu, y
            // compris pour un enfant que personne ne verra.
            var size = child.DesiredSize;
            var top = Math.Max(0d, (finalSize.Height - size.Height) / 2d);

            if (!keep)
            {
                // Hors du champ, à sa taille NATURELLE : le rognage du panneau l'empêche de peindre
                // quoi que ce soit, et son texte reste mesuré à sa vraie largeur.
                child.Arrange(new Rect(finalSize.Width + spacing, top, size.Width, size.Height));

                continue;
            }

            child.Arrange(new Rect(x, top, size.Width, size.Height));
            x += size.Width + spacing;
        }

        return finalSize;
    }

    /// <summary>
    /// Décide, pour une largeur donnée, combien d'enfants ordinaires tiennent en entier. Mesure
    /// TOUJOURS chaque enfant sans la moindre contrainte : la décision porte sur des tailles
    /// naturelles, jamais sur des tailles déjà bornées par la place restante, et la contrainte
    /// identique à la mesure et à l'arrangement fait de la seconde passe un simple rappel.
    /// </summary>
    private RowPlan Plan(double availableWidth)
    {
        var spacing = Math.Max(0d, Spacing);
        var constraint = Size.Infinity;
        var limit = double.IsInfinity(availableWidth) || double.IsNaN(availableWidth) ? double.PositiveInfinity : availableWidth;

        Control? indicator = null;
        var ordinary = new List<Control>(Children.Count);

        // La hauteur de la rangée est celle de la plus haute pastille VISIBLE, écartées comprises :
        // le fait qu'une pastille ne tienne pas en largeur n'a aucune raison de raccourcir la
        // rangée, et une rangée qui change de hauteur avec la largeur ferait sauter le pied de la
        // carte d'une taille de fenêtre à l'autre.
        var height = 0d;

        foreach (var child in Children)
        {
            child.Measure(constraint);
            if (!child.IsVisible)
            {
                // Une pastille que le ViewModel a masquée ne prend ni place ni gouttière.
                continue;
            }

            if (GetIsOverflowIndicator(child))
            {
                indicator ??= child;

                continue;
            }

            height = Math.Max(height, child.DesiredSize.Height);
            ordinary.Add(child);
        }

        var kept = Fit(ordinary, limit, spacing, out var width);
        if (kept == ordinary.Count || indicator is null)
        {
            return new RowPlan(kept, ordinary.Count - kept, width, height);
        }

        // Quelque chose sera écarté, donc l'indicateur sera rendu : sa place se réserve AVANT de
        // trancher, sinon il déborderait à son tour.
        var reserved = Math.Max(0d, limit - indicator.DesiredSize.Width - spacing);
        var keptWithIndicator = Fit(ordinary, reserved, spacing, out width);

        return new RowPlan(
            keptWithIndicator,
            ordinary.Count - keptWithIndicator,
            Math.Min(limit, keptWithIndicator == 0 ? indicator.DesiredSize.Width : width + spacing + indicator.DesiredSize.Width),
            Math.Max(height, indicator.DesiredSize.Height));
    }

    private static int Fit(List<Control> children, double limit, double spacing, out double width)
    {
        width = 0d;

        var kept = 0;
        foreach (var child in children)
        {
            var candidate = kept == 0 ? child.DesiredSize.Width : width + spacing + child.DesiredSize.Width;
            if (candidate > limit + Epsilon)
            {
                break;
            }

            width = candidate;
            kept++;
        }

        return kept;
    }

    private readonly record struct RowPlan(int Kept, int Dropped, double Width, double Height);
}