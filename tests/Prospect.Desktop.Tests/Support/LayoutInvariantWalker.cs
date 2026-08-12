using System.Globalization;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

using Prospect.Desktop.Layout;

namespace Prospect.Desktop.Tests.Support;

/// <summary>Les trois invariants de boîte vérifiés par <see cref="LayoutInvariantWalker"/>.</summary>
internal enum LayoutInvariant
{
    /// <summary>Une boîte peinte sort de la fenêtre (ou de l'ancêtre qui la rogne).</summary>
    Containment,

    /// <summary>Deux enfants d'un même panneau se recouvrent sans l'avoir déclaré.</summary>
    Overlap,

    /// <summary>Un texte plus large que sa place, sans troncature ni retour à la ligne.</summary>
    TextOverflow,
}

/// <summary>
/// Une violation d'invariant, nommée par le chemin du contrôle fautif dans l'arbre visuel et par
/// les rectangles en cause (coordonnées fenêtre), pour qu'un échec de test désigne la vue à
/// corriger sans qu'il faille relancer l'application.
/// </summary>
internal sealed record LayoutViolation(LayoutInvariant Invariant, string Path, string Detail)
{
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"[{Invariant}] {Path}\n        {Detail}");
}

/// <summary>
/// Arpenteur d'invariants de mise en page : parcourt l'arbre visuel d'une fenêtre AFFICHÉE et
/// arrangée, et rapporte chaque boîte qui dépasse, chevauche, ou tronque du texte en silence. Il
/// existe parce que l'œil rate ces défauts (ils n'apparaissent qu'à certaines largeurs de fenêtre,
/// sur certains états de données) alors qu'une mesure de rectangles ne les rate pas.
///
/// Trois invariants, indépendants :
///
/// <b>A. Confinement.</b> Les bornes de chaque visuel visible, transformées en coordonnées fenêtre
/// puis intersectées avec la chaîne de rognage de ses ancêtres, restent dans la fenêtre ET dans la
/// boîte de leur parent. La seconde moitié est celle qui compte : un panneau horizontal mesure ses
/// enfants sans contrainte de largeur, puis les arrange à la queue leu leu — le dernier sort de la
/// boîte du panneau et va se peindre par-dessus ce qui suit, sans que ni le panneau ni l'enfant
/// n'aient l'air fautifs pris isolément. C'est exactement le défaut signalé en test réel, et il est
/// invisible tant qu'on ne compare pas l'enfant à la boîte de son parent.
///
/// Le débordement hors du parent ne se juge que sur les nœuds des vues : à l'intérieur d'un gabarit
/// de contrôle, une partie qui dépasse est courante et voulue (le curseur d'un interrupteur glisse
/// hors de sa piste). Le débordement hors de la FENÊTRE, lui, reste vérifié partout.
///
/// Prendre les bornes ROGNÉES est ce qui rend l'invariant juste sur un <see cref="ScrollViewer"/> :
/// le contenu défilable est jugé sur sa partie visible, pas sur son étendue totale, exactement comme
/// l'utilisateur le voit. Les racines qui vivent hors du flux par construction (popups, infobulles,
/// couches d'overlay et d'adorners) sont exemptées : elles ont leur propre monde de coordonnées, et
/// un menu déroulant qui déborde de la fenêtre est une décision de placement de la plateforme.
///
/// <b>B. Non-chevauchement.</b> Deux enfants directs d'un même panneau de disposition ne
/// s'intersectent pas de plus d'un pixel. Tous les <see cref="Panel"/> comptent (y compris le
/// <see cref="Panel"/> nu, dont l'empilement est justement ce qu'on veut voir déclaré), sauf le
/// <see cref="Canvas"/>, où le positionnement absolu rend la question vide de sens. Les internes de
/// ControlTheme sont hors périmètre (<c>TemplatedParent</c> non nul) : un gabarit de contrôle
/// superpose légitimement ses parties (piste et remplissage d'une barre de progression, filigrane
/// d'un champ de saisie), ce que l'invariant ne saurait distinguer d'un défaut. Ce que cet
/// invariant juge, c'est la composition des vues — le code qu'on écrit et qu'on casse.
/// Les superpositions voulues se déclarent dans l'AXAML via <see cref="LayoutContract"/>.
///
/// <b>C. Textes.</b> Tout <see cref="TextBlock"/> dont la mise en page de texte excède la place
/// qu'on lui a arrangée, alors qu'il n'a ni <c>TextTrimming</c> ni <c>TextWrapping</c>, est une
/// violation :
/// son texte se fait couper net par le premier ancêtre qui rogne, sans point de suspension, et
/// l'utilisateur ne peut même pas savoir qu'il manque quelque chose. La correction se choisit au
/// cas par cas (troncature plus infobulle pour une valeur machine, retour à la ligne pour de la
/// prose), l'arpenteur ne fait que refuser le silence.
/// </summary>
internal static class LayoutInvariantWalker
{
    /// <summary>
    /// Un pixel de tolérance : les arrondis de mise en page (pixel snapping, mesures de texte)
    /// produisent des écarts sous-pixel qu'aucun utilisateur ne voit et qu'aucune correction ne
    /// peut supprimer.
    /// </summary>
    private const double Epsilon = 1.0;

    /// <summary>
    /// Chaîne de rognage vide au départ de l'arpentage : un rectangle largement plus grand que
    /// toute fenêtre plausible, plutôt qu'un infini, dont l'arithmétique de <see cref="Rect"/>
    /// (bord droit = X + largeur) ferait un NaN qui contaminerait toutes les intersections.
    /// </summary>
    private static readonly Rect Unbounded = new(-1_000_000, -1_000_000, 2_000_000, 2_000_000);

    /// <summary>
    /// Arpente <paramref name="window"/> et rend toutes les violations trouvées. La fenêtre doit
    /// être affichée et arrangée (voir <c>HeadlessTestHelpers.Settle</c>) : sans passe de mise en
    /// page, toutes les bornes valent zéro et l'arpenteur ne trouve évidemment rien.
    /// </summary>
    public static IReadOnlyList<LayoutViolation> Inspect(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var violations = new List<LayoutViolation>();
        var windowRect = new Rect(window.Bounds.Size);
        Walk(window, window, windowRect, windowRect, Unbounded, string.Empty, 0, violations);

        return violations;
    }

    /// <summary>
    /// Redimensionne <paramref name="window"/> à chacune des tailles de la garde
    /// (<see cref="ResponsiveWindowSizes.All"/>), l'arpente, et échoue une seule fois avec la
    /// liste complète des violations : un écran cassé aux trois tailles doit se corriger en une
    /// passe, pas en trois allers-retours avec la CI. <paramref name="scenario"/> nomme l'écran et
    /// son état.
    /// </summary>
    public static void ShouldHoldLayoutInvariantsAtEverySize(this Window window, string scenario)
    {
        ArgumentNullException.ThrowIfNull(window);

        var report = new StringBuilder();
        var total = 0;

        foreach (var size in ResponsiveWindowSizes.All)
        {
            window.Width = size.Width;
            window.Height = size.Height;
            window.Settle();

            var violations = Inspect(window);
            total += violations.Count;
            if (violations.Count == 0)
            {
                continue;
            }

            report.Append("  ").Append(scenario).Append(" @ ")
                .Append(size.Width.ToString(CultureInfo.InvariantCulture)).Append('x')
                .Append(size.Height.ToString(CultureInfo.InvariantCulture))
                .Append(" : ").Append(violations.Count).AppendLine(" violation(s)");
            foreach (var violation in violations)
            {
                report.Append("    ").AppendLine(violation.ToString());
            }
        }

        if (total > 0)
        {
            Assert.Fail($"{total} violation(s) d'invariant de mise en page{Environment.NewLine}{report}");
        }
    }

    private static void Walk(
        Visual visual,
        Visual window,
        Rect windowRect,
        Rect parentRect,
        Rect inheritedClip,
        string parentPath,
        int index,
        List<LayoutViolation> violations)
    {
        if (!visual.IsVisible)
        {
            return;
        }

        if (IsOutOfFlowRoot(visual))
        {
            return;
        }

        if (visual.TransformToVisual(window) is not { } transform)
        {
            return;
        }

        var path = Join(parentPath, Describe(visual, index));

        // La tuyauterie des gabarits de contrôle (présentateurs, bordures PART_*, couches du
        // TopLevel) disparaît du chemin transmis aux enfants : le chemin doit ramener à l'AXAML
        // qu'on écrit, pas décrire l'intérieur des ControlThemes. Le nœud fautif, lui, reste
        // toujours nommé en bout de chemin.
        var childPath = IsTemplatePlumbing(visual) ? parentPath : path;

        var rect = new Rect(visual.Bounds.Size).TransformToAABB(transform);
        var painted = rect.Intersect(inheritedClip);

        if (!ReferenceEquals(visual, window) && HasArea(painted))
        {
            if (!IsInside(painted, windowRect))
            {
                violations.Add(new LayoutViolation(
                    LayoutInvariant.Containment,
                    path,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"peint en {Format(painted)}, déborde de la fenêtre {Format(windowRect)}")));
            }
            else if (!IsTemplatePlumbing(visual) && !IsInside(painted, parentRect))
            {
                violations.Add(new LayoutViolation(
                    LayoutInvariant.Containment,
                    path,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"peint en {Format(painted)}, déborde de la boîte de son parent {Format(parentRect)}")));
            }
        }

        if (visual is TextBlock text)
        {
            CheckText(text, path, violations);
        }

        var childClip = inheritedClip;
        if (visual.ClipToBounds)
        {
            childClip = childClip.Intersect(rect);
        }

        if (visual.Clip is { } clipGeometry)
        {
            childClip = childClip.Intersect(clipGeometry.Bounds.TransformToAABB(transform));
        }

        var children = visual.GetVisualChildren().ToList();

        if (visual is Panel panel)
        {
            CheckOverlap(panel, children, window, childClip, path, violations);
        }

        for (var child = 0; child < children.Count; child++)
        {
            Walk(children[child], window, windowRect, rect, childClip, childPath, child, violations);
        }
    }

    /// <summary>
    /// Invariant C : un texte dont la mise en page naturelle dépasse la place arrangée, alors que
    /// rien ne l'autorise à se tronquer ni à passer à la ligne.
    ///
    /// La comparaison se fait sur le <see cref="TextBlock.TextLayout"/> et non sur
    /// <c>DesiredSize</c> : Avalonia borne la taille désirée à la taille disponible, donc un texte
    /// mesuré sous une contrainte trop étroite rend une taille désirée qui a l'air de tenir. Le
    /// TextLayout, lui, dit la vraie largeur des lignes — sans retour à la ligne, c'est la largeur
    /// naturelle du texte entier, quelle qu'ait été la contrainte.
    /// </summary>
    private static void CheckText(TextBlock text, string path, List<LayoutViolation> violations)
    {
        if (text.TextWrapping != TextWrapping.NoWrap || text.TextTrimming != TextTrimming.None)
        {
            return;
        }

        var padding = text.Padding;
        var needed = new Size(
            text.TextLayout.Width + padding.Left + padding.Right,
            text.TextLayout.Height + padding.Top + padding.Bottom);

        if (needed.Width <= text.Bounds.Width + Epsilon && needed.Height <= text.Bounds.Height + Epsilon)
        {
            return;
        }

        violations.Add(new LayoutViolation(
            LayoutInvariant.TextOverflow,
            path,
            string.Create(
                CultureInfo.InvariantCulture,
                $"« {Shorten(text.Text)} » veut {needed.Width:0.#}x{needed.Height:0.#} dans {text.Bounds.Width:0.#}x{text.Bounds.Height:0.#}, sans TextTrimming ni TextWrapping")));
    }

    /// <summary>
    /// Invariant B : recouvrement entre enfants directs d'un panneau. Les rectangles comparés sont
    /// les rectangles PEINTS (bornes intersectées avec la chaîne de rognage), pour qu'un contenu à
    /// moitié sorti d'un <see cref="ScrollViewer"/> ne soit pas accusé de recouvrir ce qui le suit.
    /// </summary>
    private static void CheckOverlap(
        Panel panel,
        List<Visual> children,
        Visual window,
        Rect childClip,
        string path,
        List<LayoutViolation> violations)
    {
        if (panel is Canvas || panel.TemplatedParent is not null)
        {
            return;
        }

        if (panel.GetValue(LayoutContract.AllowsOverlapProperty))
        {
            return;
        }

        var boxes = new List<(int Index, Visual Visual, Rect Rect)>();
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            if (!child.IsVisible || IsOutOfFlowRoot(child) || child.TransformToVisual(window) is not { } transform)
            {
                continue;
            }

            var rect = new Rect(child.Bounds.Size).TransformToAABB(transform).Intersect(childClip);
            if (HasArea(rect))
            {
                boxes.Add((index, child, rect));
            }
        }

        for (var left = 0; left < boxes.Count; left++)
        {
            for (var right = left + 1; right < boxes.Count; right++)
            {
                var intersection = boxes[left].Rect.Intersect(boxes[right].Rect);
                if (intersection.Width <= Epsilon || intersection.Height <= Epsilon)
                {
                    continue;
                }

                if (Declares(boxes[left].Visual) || Declares(boxes[right].Visual))
                {
                    continue;
                }

                violations.Add(new LayoutViolation(
                    LayoutInvariant.Overlap,
                    path,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{Describe(boxes[left].Visual, boxes[left].Index)} {Format(boxes[left].Rect)} recouvre "
                        + $"{Describe(boxes[right].Visual, boxes[right].Index)} {Format(boxes[right].Rect)} "
                        + $"sur {Format(intersection)}")));
            }
        }
    }

    private static bool Declares(Visual visual)
        => visual is Control control && control.GetValue(LayoutContract.AllowsOverlapProperty);

    /// <summary>
    /// Racines exemptées : elles ne participent pas au flux de la fenêtre. Un popup (menu, flyout)
    /// et une infobulle sont placés par la plateforme, éventuellement hors de la fenêtre ; les
    /// couches d'overlay et d'adorners sont des calques de service, vides tant que rien ne les
    /// utilise.
    /// </summary>
    private static bool IsOutOfFlowRoot(Visual visual)
        => visual is Popup or PopupRoot or OverlayPopupHost or OverlayLayer or AdornerLayer
            or LightDismissOverlayLayer or ChromeOverlayLayer or ToolTip;

    private static bool HasArea(Rect rect)
        => rect.Width > Epsilon && rect.Height > Epsilon;

    private static bool IsInside(Rect inner, Rect outer)
        => inner.X >= outer.X - Epsilon
            && inner.Y >= outer.Y - Epsilon
            && inner.Right <= outer.Right + Epsilon
            && inner.Bottom <= outer.Bottom + Epsilon;

    /// <summary>
    /// Nœuds nés d'un gabarit de contrôle, ou couches de service du <see cref="TopLevel"/> : ils
    /// n'apparaissent dans aucun AXAML de vue, donc les nommer dans un chemin n'aide personne.
    /// </summary>
    private static bool IsTemplatePlumbing(Visual visual)
        => visual is VisualLayerManager || (visual as StyledElement)?.TemplatedParent is not null;

    private static string Join(string parentPath, string segment)
        => parentPath.Length == 0 ? segment : $"{parentPath} > {segment}";

    private static string Describe(Visual visual, int index)
    {
        var descriptor = new StringBuilder(visual.GetType().Name);
        if (visual is StyledElement { Name: { Length: > 0 } name })
        {
            descriptor.Append('#').Append(name);
        }

        if (visual is Control control)
        {
            // Les pseudo-classes (:pointerover, :focus...) décrivent un état, pas l'identité du
            // contrôle : les garder rendrait le chemin instable d'une exécution à l'autre.
            foreach (var className in control.Classes.Where(c => c.Length > 0 && c[0] != ':'))
            {
                descriptor.Append('.').Append(className);
            }
        }

        descriptor.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append(']');

        return descriptor.ToString();
    }

    private static string Format(Rect rect)
        => string.Create(CultureInfo.InvariantCulture, $"({rect.X:0.#},{rect.Y:0.#} {rect.Width:0.#}x{rect.Height:0.#})");

    private static string Shorten(string? value)
        => value is null ? string.Empty
            : value.Length <= 40 ? value
            : value[..40] + "…";
}

/// <summary>
/// Les tailles auxquelles la garde s'exécute. <see cref="Floor"/> est le plancher tenu par la
/// fenêtre principale (ses <c>MinWidth</c>/<c>MinHeight</c>) : en deçà, l'utilisateur ne peut pas
/// descendre, donc il n'y a rien à garantir ; juste au-dessus, c'est la situation la plus
/// contrainte que la mise en page doive absorber. Les deux autres couvrent une fenêtre de travail
/// ordinaire et la taille de référence du design (1280x800).
/// </summary>
internal static class ResponsiveWindowSizes
{
    public static readonly Size Floor = new(960, 600);

    public static readonly IReadOnlyList<Size> All = [Floor, new Size(1100, 700), new Size(1280, 800)];
}