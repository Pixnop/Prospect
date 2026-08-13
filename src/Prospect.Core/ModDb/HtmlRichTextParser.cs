using System.Net;
using System.Text;

namespace Prospect.Core.ModDb;

/// <summary>
/// Traduit le HTML d'éditeur riche d'une fiche ModDB en <see cref="RichTextDocument"/>, le modèle
/// de blocs que la couche UI compose ensuite en contrôles. Calcul pur, sans la moindre dépendance
/// d'interface : c'est ce qui le rend testable sur des fiches réelles.
/// </summary>
/// <remarks>
/// <para>
/// Prospect ne rend PAS du HTML : il en lit un sous-ensemble et le retranscrit. Le champ
/// <c>text</c> vient d'un éditeur WYSIWYG public, donc d'un tiers, et embarquer un moteur de rendu
/// HTML pour l'afficher reviendrait à exécuter du balisage arbitraire dans le launcher. Ce
/// parseur pose la frontière inverse : il ne comprend que ce qu'un texte de présentation a besoin
/// de dire, et tout le reste dégrade.
/// </para>
/// <para>
/// Les trois règles de dégradation, dans cet ordre. Le contenu de
/// <c>script</c>/<c>style</c>/<c>iframe</c>/<c>noscript</c>/<c>head</c>/<c>svg</c>/<c>object</c>/<c>embed</c>
/// est JETÉ avec sa balise : ce n'est pas du texte destiné au lecteur. Une balise CONNUE est
/// traduite. Une balise inconnue (<c>span</c>, <c>font</c>, <c>table</c>...) disparaît mais son
/// texte reste, ce qui est le comportement le moins destructeur : une fiche qui abuse d'un
/// balisage exotique reste lisible.
/// </para>
/// <para>
/// La tolérance aux balises mal fermées est délibérée : les enrichissements sont comptés
/// (un compteur par style) plutôt qu'empilés, donc un <c>&lt;/strong&gt;</c> orphelin ne met pas
/// tout le reste du document en gras et un <c>&lt;strong&gt;</c> jamais fermé s'arrête à la fin de
/// son bloc. Cette HTML-là n'est pas produite par un compilateur, elle est tapée par des joueurs.
/// </para>
/// </remarks>
public static class HtmlRichTextParser
{
    /// <summary>Balises dont le CONTENU est jeté avec la balise.</summary>
    private static readonly HashSet<string> DroppedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "iframe", "noscript", "svg", "object", "embed",
    };

    /// <summary>Balises qui ferment le bloc courant sans rien produire d'autre.</summary>
    private static readonly HashSet<string> BlockBoundaryTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "main", "aside",
        "blockquote", "table", "thead", "tbody", "tr", "td", "th", "figure", "figcaption",
    };

    /// <summary>Balises vides : elles n'ont jamais de fermeture à attendre.</summary>
    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "hr", "img", "input", "meta", "link", "source", "col", "area", "base", "wbr",
    };

    /// <summary>
    /// Lit <paramref name="html"/> et rend ses blocs. Une entrée nulle, vide ou entièrement
    /// blanche rend <see cref="RichTextDocument.Empty"/> ; cette méthode ne lève jamais, quel que
    /// soit le balisage reçu.
    /// </summary>
    public static RichTextDocument Parse(string? html)
        => string.IsNullOrWhiteSpace(html) ? RichTextDocument.Empty : new Parser(html).Run();

    // Machine à états sur un flux de balises, sans arbre DOM intermédiaire : le document est écrit
    // au fil de la lecture, ce qui suffit pour la grammaire couverte et évite de garder en mémoire
    // deux représentations d'une description de 29 Ko.
    private sealed class Parser(string html)
    {
        private readonly List<RichTextBlock> _blocks = [];
        private readonly List<RichTextRun> _runs = [];
        private readonly Stack<ListFrame> _lists = new();
        private readonly Stack<Uri?> _links = new();
        private readonly StringBuilder _pending = new();
        private readonly StringBuilder _preformatted = new();
        private readonly Dictionary<RichTextStyle, int> _styles = [];

        private int _headingLevel;
        private bool _inPreformatted;
        private bool _pendingSeparator;

        public RichTextDocument Run()
        {
            var index = 0;
            while (index < html.Length)
            {
                var open = html.IndexOf('<', index);
                if (open < 0)
                {
                    AppendText(html.AsSpan(index));

                    break;
                }

                AppendText(html.AsSpan(index, open - index));

                if (html.AsSpan(open).StartsWith("<!--", StringComparison.Ordinal))
                {
                    var comment = html.IndexOf("-->", open, StringComparison.Ordinal);
                    index = comment < 0 ? html.Length : comment + 3;

                    continue;
                }

                var close = FindTagEnd(open);
                if (close < 0)
                {
                    // Un '<' qui n'ouvre aucune balise complète est du texte : « la version 3 < 4 »
                    // ne doit pas avaler le reste de la description.
                    AppendText(html.AsSpan(open));

                    break;
                }

                index = HandleTag(open, close);
            }

            FlushBlock();
            CloseAllLists();

            return _blocks.Count == 0 ? RichTextDocument.Empty : new RichTextDocument(_blocks);
        }

        // ── Lecture des balises ──────────────────────────────────────────────────────────────

        private int HandleTag(int open, int close)
        {
            var isClosing = open + 1 < html.Length && html[open + 1] == '/';
            var name = ReadTagName(open + (isClosing ? 2 : 1), close);

            if (name.Length == 0)
            {
                // Un commentaire (<!-- ... -->) ou une déclaration : rien à produire.
                return close + 1;
            }

            if (DroppedTags.Contains(name))
            {
                return isClosing ? close + 1 : SkipDroppedContent(name, close + 1);
            }

            if (_inPreformatted && !(isClosing && string.Equals(name, "pre", StringComparison.OrdinalIgnoreCase)))
            {
                // Dans un <pre>, le balisage interne n'est que du bruit : seul son texte compte,
                // et la fermeture du <pre> est la seule balise qui ait un effet.
                return close + 1;
            }

            if (isClosing)
            {
                HandleClosingTag(name);
            }
            else
            {
                HandleOpeningTag(name, open, close);
            }

            return close + 1;
        }

        private void HandleOpeningTag(string name, int open, int close)
        {
            switch (name.ToLowerInvariant())
            {
                case "br":
                    _runs.Add(RichTextRun.LineBreak);
                    _pendingSeparator = false;

                    break;

                case "hr":
                    FlushBlock();
                    Emit(new RichTextRule());

                    break;

                case "img":
                    EmitImage(html[open..(close + 1)]);

                    break;

                case "a":
                    _links.Push(ModDbMapper.ToExternalUri(ReadAttribute(html[open..(close + 1)], "href")));

                    break;

                case "pre":
                    FlushBlock();
                    _inPreformatted = true;
                    _preformatted.Clear();

                    break;

                case "ul":
                    OpenList(isOrdered: false);

                    break;

                case "ol":
                    OpenList(isOrdered: true);

                    break;

                case "li":
                    FlushListItem();

                    break;

                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    FlushBlock();
                    _headingLevel = name[1] - '0';

                    break;

                case "strong" or "b":
                    AddStyle(RichTextStyle.Bold);

                    break;

                case "em" or "i":
                    AddStyle(RichTextStyle.Italic);

                    break;

                case "u" or "ins":
                    AddStyle(RichTextStyle.Underline);

                    break;

                case "s" or "strike" or "del":
                    AddStyle(RichTextStyle.Strikethrough);

                    break;

                case "code":
                    AddStyle(RichTextStyle.Code);

                    break;

                default:
                    if (BlockBoundaryTags.Contains(name))
                    {
                        FlushBlock();
                    }

                    // Toute autre balise (span, font, abbr...) disparaît en laissant son texte.
                    break;
            }
        }

        private void HandleClosingTag(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "a":
                    if (_links.Count > 0)
                    {
                        _links.Pop();
                    }

                    break;

                case "pre":
                    EmitPreformatted();

                    break;

                case "ul" or "ol":
                    CloseList();

                    break;

                case "li":
                    FlushListItem();

                    break;

                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    FlushBlock();

                    break;

                case "strong" or "b":
                    RemoveStyle(RichTextStyle.Bold);

                    break;

                case "em" or "i":
                    RemoveStyle(RichTextStyle.Italic);

                    break;

                case "u" or "ins":
                    RemoveStyle(RichTextStyle.Underline);

                    break;

                case "s" or "strike" or "del":
                    RemoveStyle(RichTextStyle.Strikethrough);

                    break;

                case "code":
                    RemoveStyle(RichTextStyle.Code);

                    break;

                default:
                    if (BlockBoundaryTags.Contains(name))
                    {
                        FlushBlock();
                    }

                    break;
            }
        }

        // ── Texte ────────────────────────────────────────────────────────────────────────────

        private void AppendText(ReadOnlySpan<char> raw)
        {
            if (raw.Length == 0)
            {
                return;
            }

            var decoded = WebUtility.HtmlDecode(raw.ToString());

            if (_inPreformatted)
            {
                _preformatted.Append(decoded);

                return;
            }

            var style = CurrentStyle();
            var link = CurrentLink();

            // Les retours à la ligne du source HTML sont de l'indentation, pas de la mise en forme :
            // seul <br> coupe une ligne. Tout blanc se réduit donc à une espace, et deux blancs
            // consécutifs à une seule, y compris de part et d'autre d'une frontière de balise.
            foreach (var character in decoded)
            {
                if (char.IsWhiteSpace(character))
                {
                    _pendingSeparator = _pending.Length > 0 || _runs.Count > 0;

                    continue;
                }

                if (_pendingSeparator)
                {
                    AppendSeparator(style, link);
                    _pendingSeparator = false;
                }

                _pending.Append(character);
            }

            FlushPending(style, link);
        }

        /// <summary>
        /// Pose l'espace qui sépare deux fragments. Il revient au fragment PRÉCÉDENT quand le style
        /// ou le lien change : sinon l'espace de « voir <c>&lt;a&gt;</c>la doc » se retrouverait
        /// dans le lien, donc souligné et cliquable alors qu'il n'appartient pas au libellé.
        /// </summary>
        private void AppendSeparator(RichTextStyle style, Uri? link)
        {
            if (_pending.Length == 0
                && _runs.Count > 0
                && _runs[^1] is { IsLineBreak: false } previous
                && (previous.Style != style || previous.Link != link))
            {
                _runs[^1] = previous with { Text = previous.Text + " " };

                return;
            }

            _pending.Append(' ');
        }

        // Le texte accumulé devient un fragment dès que le style ou le lien courant change, ce que
        // ce point d'appel garantit : il est invoqué à chaque bout de texte, avec l'état du moment.
        private void FlushPending(RichTextStyle style, Uri? link)
        {
            if (_pending.Length == 0)
            {
                return;
            }

            var text = _pending.ToString();
            _pending.Clear();

            if (_runs.Count > 0 && _runs[^1] is { IsLineBreak: false } last && last.Style == style && last.Link == link)
            {
                _runs[^1] = last with { Text = last.Text + text };

                return;
            }

            _runs.Add(new RichTextRun(text, style, link));
        }

        // ── Styles et liens ──────────────────────────────────────────────────────────────────

        private void AddStyle(RichTextStyle style) => _styles[style] = _styles.GetValueOrDefault(style) + 1;

        private void RemoveStyle(RichTextStyle style)
        {
            var depth = _styles.GetValueOrDefault(style);
            _styles[style] = depth > 0 ? depth - 1 : 0;
        }

        private RichTextStyle CurrentStyle()
        {
            var style = RichTextStyle.None;
            foreach (var (candidate, depth) in _styles)
            {
                if (depth > 0)
                {
                    style |= candidate;
                }
            }

            return style;
        }

        private Uri? CurrentLink()
        {
            foreach (var link in _links)
            {
                if (link is not null)
                {
                    return link;
                }
            }

            return null;
        }

        // ── Blocs ────────────────────────────────────────────────────────────────────────────

        private void Emit(RichTextBlock block)
        {
            if (_lists.Count > 0)
            {
                _lists.Peek().PendingChildren.Add(block);

                return;
            }

            _blocks.Add(block);
        }

        private void FlushBlock()
        {
            _pendingSeparator = false;
            var runs = TakeRuns();
            if (runs.Count == 0)
            {
                _headingLevel = 0;

                return;
            }

            if (_lists.Count > 0)
            {
                _lists.Peek().PendingRuns.AddRange(runs);
                _headingLevel = 0;

                return;
            }

            Emit(_headingLevel > 0 ? new RichTextHeading(_headingLevel, runs) : new RichTextParagraph(runs));
            _headingLevel = 0;
        }

        // Rend les fragments accumulés, débarrassés des blancs de bordure et des blocs qui ne
        // porteraient que du vide (<p>&nbsp;</p>, très courant comme espaceur sur les fiches).
        private List<RichTextRun> TakeRuns()
        {
            FlushPending(CurrentStyle(), CurrentLink());

            var runs = new List<RichTextRun>(_runs);
            _runs.Clear();

            while (runs.Count > 0 && IsBlank(runs[0]))
            {
                runs.RemoveAt(0);
            }

            while (runs.Count > 0 && IsBlank(runs[^1]))
            {
                runs.RemoveAt(runs.Count - 1);
            }

            return runs.Any(run => run.IsLineBreak || run.Text.Trim().Length > 0) ? runs : [];
        }

        private static bool IsBlank(RichTextRun run) => run.IsLineBreak || run.Text.Trim().Length == 0;

        private void EmitPreformatted()
        {
            _inPreformatted = false;
            var text = _preformatted.ToString().Trim('\r', '\n').TrimEnd();
            _preformatted.Clear();

            if (text.Trim().Length > 0)
            {
                Emit(new RichTextCodeBlock(text));
            }
        }

        private void EmitImage(string tag)
        {
            if (ModDbMapper.ToExternalUri(ReadAttribute(tag, "src")) is not { } source)
            {
                return;
            }

            FlushBlock();
            Emit(new RichTextImage(source, ReadAttribute(tag, "alt") ?? string.Empty, CurrentLink()));
        }

        // ── Listes ───────────────────────────────────────────────────────────────────────────

        private void OpenList(bool isOrdered)
        {
            FlushBlock();
            _lists.Push(new ListFrame(isOrdered));
        }

        private void CloseList()
        {
            if (_lists.Count == 0)
            {
                return;
            }

            FlushListItem();
            var frame = _lists.Pop();
            if (frame.Items.Count > 0)
            {
                Emit(new RichTextList(frame.IsOrdered, frame.Items));
            }
        }

        private void CloseAllLists()
        {
            while (_lists.Count > 0)
            {
                CloseList();
            }
        }

        // Une entrée se ferme aussi bien sur </li> que sur le <li> suivant : les fiches réelles
        // omettent régulièrement la fermeture.
        private void FlushListItem()
        {
            if (_lists.Count == 0)
            {
                return;
            }

            _pendingSeparator = false;
            var frame = _lists.Peek();
            frame.PendingRuns.AddRange(TakeRuns());

            if (frame.PendingRuns.Count == 0 && frame.PendingChildren.Count == 0)
            {
                return;
            }

            frame.Items.Add(new RichTextListItem([.. frame.PendingRuns], [.. frame.PendingChildren]));
            frame.PendingRuns.Clear();
            frame.PendingChildren.Clear();
        }

        private sealed class ListFrame(bool isOrdered)
        {
            public bool IsOrdered { get; } = isOrdered;

            public List<RichTextListItem> Items { get; } = [];

            public List<RichTextRun> PendingRuns { get; } = [];

            public List<RichTextBlock> PendingChildren { get; } = [];
        }

        // ── Lexique ──────────────────────────────────────────────────────────────────────────

        // Le '>' de fin est cherché HORS guillemets : un attribut peut en contenir
        // (alt="a > b"), et s'arrêter au premier ferait passer la fin de la balise pour du texte.
        private int FindTagEnd(int open)
        {
            var quote = '\0';
            for (var index = open + 1; index < html.Length; index++)
            {
                var character = html[index];
                if (quote != '\0')
                {
                    if (character == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (character is '"' or '\'')
                {
                    quote = character;
                }
                else if (character == '>')
                {
                    return index;
                }
            }

            return -1;
        }

        private string ReadTagName(int start, int end)
        {
            var index = start;
            while (index < end && char.IsAsciiLetterOrDigit(html[index]))
            {
                index++;
            }

            return html[start..index];
        }

        private int SkipDroppedContent(string name, int from)
        {
            if (VoidTags.Contains(name))
            {
                return from;
            }

            var index = from;
            while (index < html.Length)
            {
                var open = html.IndexOf('<', index);
                if (open < 0)
                {
                    return html.Length;
                }

                var close = FindTagEnd(open);
                if (close < 0)
                {
                    return html.Length;
                }

                if (open + 1 < html.Length
                    && html[open + 1] == '/'
                    && string.Equals(ReadTagName(open + 2, close), name, StringComparison.OrdinalIgnoreCase))
                {
                    return close + 1;
                }

                index = close + 1;
            }

            return html.Length;
        }

        /// <summary>
        /// Valeur d'un attribut dans le texte brut d'une balise. Écrit à la main plutôt qu'en
        /// expression régulière : la surface est minuscule (trois attributs lus en tout) et un
        /// motif sur du HTML libre est une source d'échecs plus discrète qu'une boucle.
        /// </summary>
        private static string? ReadAttribute(string tag, string attribute)
        {
            var index = 0;
            while (index < tag.Length)
            {
                var found = tag.IndexOf(attribute, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    return null;
                }

                index = found + attribute.Length;

                // Le nom doit être précédé d'un blanc : sans ça, « href » se ferait trouver dans
                // « data-href » et « src » dans « srcset ».
                if (found == 0 || !char.IsWhiteSpace(tag[found - 1]))
                {
                    continue;
                }

                var cursor = index;
                while (cursor < tag.Length && char.IsWhiteSpace(tag[cursor]))
                {
                    cursor++;
                }

                if (cursor >= tag.Length || tag[cursor] != '=')
                {
                    continue;
                }

                cursor++;
                while (cursor < tag.Length && char.IsWhiteSpace(tag[cursor]))
                {
                    cursor++;
                }

                if (cursor >= tag.Length)
                {
                    return null;
                }

                var quote = tag[cursor];
                if (quote is '"' or '\'')
                {
                    var end = tag.IndexOf(quote, cursor + 1);

                    return end < 0 ? null : WebUtility.HtmlDecode(tag[(cursor + 1)..end]);
                }

                var stop = cursor;
                while (stop < tag.Length && !char.IsWhiteSpace(tag[stop]) && tag[stop] != '>')
                {
                    stop++;
                }

                return WebUtility.HtmlDecode(tag[cursor..stop]);
            }

            return null;
        }
    }
}
