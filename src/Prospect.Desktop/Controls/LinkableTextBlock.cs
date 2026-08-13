using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

using Prospect.Core.ModDb;

namespace Prospect.Desktop.Controls;

/// <summary>
/// Un <see cref="TextBlock"/> dont certains fragments sont cliquables : le paragraphe d'une
/// description de fiche ModDB, liens compris.
/// </summary>
/// <remarks>
/// <para>
/// La destination est retrouvée en HIT-TESTANT la mise en page du texte plutôt qu'en logeant un
/// bouton dans le flux (<c>InlineUIContainer</c>). Le contrôle reste alors un
/// <see cref="TextBlock"/> ordinaire : il s'enroule, se mesure et se sélectionne comme tous les
/// autres, la garde de mise en page le juge avec les mêmes règles, et aucun bouton ne vient
/// perturber la ligne de base au milieu d'une phrase.
/// </para>
/// <para>
/// Les <c>&lt;br&gt;</c> deviennent des fragments <c>"\n"</c> et non des
/// <see cref="LineBreak"/> : le rendu est le même, mais un fragment de texte occupe un nombre de
/// caractères connu, donc les positions enregistrées pour les liens restent exactes. Un
/// <see cref="LineBreak"/> contribue une longueur qui n'est pas dans son contrat public, ce qui
/// décalerait la correspondance dès le premier lien qui suit un saut de ligne.
/// </para>
/// </remarks>
internal sealed class LinkableTextBlock : TextBlock
{
    private readonly List<(int Start, int Length, Uri Target)> _links = [];
    private readonly List<Run> _linkRuns = [];
    private readonly List<Run> _codeRuns = [];

    private Cursor? _handCursor;

    /// <summary>Construit le paragraphe et s'abonne aux bascules de thème.</summary>
    public LinkableTextBlock() => ActualThemeVariantChanged += (_, _) => ApplyThemeResources();

    /// <summary>Invoquée quand l'utilisateur clique un fragment porteur de lien.</summary>
    public Action<Uri>? LinkActivated { get; set; }

    /// <summary>Vrai si ce paragraphe porte au moins un lien.</summary>
    public bool HasLinks => _links.Count > 0;

    /// <summary>Remplit le paragraphe depuis les fragments analysés.</summary>
    public void SetRuns(IReadOnlyList<RichTextRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        _links.Clear();
        _linkRuns.Clear();
        _codeRuns.Clear();
        Inlines ??= [];
        Inlines.Clear();

        var offset = 0;
        foreach (var run in runs)
        {
            var text = run.IsLineBreak ? "\n" : run.Text;
            var inline = new Run(text);
            Apply(inline, run.Style, run.Link is not null);
            Inlines.Add(inline);

            if (run.Link is { } link)
            {
                _links.Add((offset, text.Length, link));
                _linkRuns.Add(inline);
            }

            if (run.Style.HasFlag(RichTextStyle.Code))
            {
                _codeRuns.Add(inline);
            }

            offset += text.Length;
        }

        // Un TextBlock au fond nul ne reçoit aucun évènement de pointeur dans Avalonia : sans ce
        // fond transparent, les liens seraient dessinés mais morts. Posé seulement s'il y a
        // quelque chose à cliquer, pour ne pas rendre tout le corps du texte capteur de clics.
        Background = _links.Count > 0 ? Brushes.Transparent : null;

        ApplyThemeResources();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyThemeResources();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (LinkActivated is not { } activate || e is null || FindLink(e.GetPosition(this)) is not { } target)
        {
            return;
        }

        e.Handled = true;
        activate(target);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_links.Count == 0 || e is null)
        {
            return;
        }

        Cursor = FindLink(e.GetPosition(this)) is null ? null : _handCursor ??= new Cursor(StandardCursorType.Hand);
    }

    /// <summary>Destination du lien sous <paramref name="point"/>, ou <see langword="null"/>.</summary>
    /// <remarks>Exposée pour que les tests headless vérifient la correspondance sans simuler de souris.</remarks>
    internal Uri? FindLink(Point point)
    {
        if (_links.Count == 0)
        {
            return null;
        }

        var hit = TextLayout.HitTestPoint(point);
        if (!hit.IsInside)
        {
            return null;
        }

        foreach (var (start, length, target) in _links)
        {
            if (hit.TextPosition >= start && hit.TextPosition < start + length)
            {
                return target;
            }
        }

        return null;
    }

    // Un Run n'est pas un Control : il ne participe pas au sélecteur de style ni à la recherche de
    // ressource depuis l'AXAML. Les deux ressources dont il a besoin sont donc résolues ici, par
    // le TextBlock qui le porte, et REposées à chaque bascule de thème — c'est exactement le
    // défaut qui avait laissé des couleurs figées en sombre sur un fond passé en clair.
    private void ApplyThemeResources()
    {
        if (_linkRuns.Count == 0 && _codeRuns.Count == 0)
        {
            return;
        }

        if (this.TryFindResource("AccentText", ActualThemeVariant, out var accent) && accent is IBrush brush)
        {
            foreach (var run in _linkRuns)
            {
                run.Foreground = brush;
            }
        }

        if (this.TryFindResource("FontMono", ActualThemeVariant, out var mono) && mono is FontFamily family)
        {
            foreach (var run in _codeRuns)
            {
                run.FontFamily = family;
            }
        }
    }

    private static void Apply(Run inline, RichTextStyle style, bool isLink)
    {
        if (style.HasFlag(RichTextStyle.Bold))
        {
            inline.FontWeight = FontWeight.SemiBold;
        }

        if (style.HasFlag(RichTextStyle.Italic))
        {
            inline.FontStyle = FontStyle.Italic;
        }

        var decorations = new TextDecorationCollection();
        if (style.HasFlag(RichTextStyle.Underline) || isLink)
        {
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        }

        if (style.HasFlag(RichTextStyle.Strikethrough))
        {
            decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        }

        if (decorations.Count > 0)
        {
            inline.TextDecorations = decorations;
        }
    }
}
