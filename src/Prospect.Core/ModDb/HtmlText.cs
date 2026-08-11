using System.Net;
using System.Text;

namespace Prospect.Core.ModDb;

/// <summary>
/// Réduit à du texte lisible la description longue d'une fiche ModDB, qui arrive en HTML brut
/// d'éditeur WYSIWYG (<c>&lt;h2&gt;</c>, <c>&lt;p&gt;</c>, <c>&lt;a href&gt;</c>, <c>&lt;img&gt;</c>
/// observés par la recherche).
/// </summary>
/// <remarks>
/// Choix assumé : Prospect n'embarque pas de moteur de rendu HTML pour ce seul usage. Afficher du
/// balisage brut serait illisible, le rendre demanderait un composant entier, et surtout du HTML
/// arbitraire venu d'un tiers n'a rien à faire dans un rendu riche à l'intérieur du launcher. La
/// fiche montre donc un texte nettoyé, et le bouton « Ouvrir sur le ModDB » renvoie à la page
/// officielle pour la version complète, images comprises.
/// </remarks>
public static class HtmlText
{
    // Marqueur interne de frontière de bloc, posé à la place des balises. Il ne peut pas s'agir
    // d'un simple '\n' : le HTML des fiches est indenté, ses propres retours à la ligne sont
    // décoratifs et doivent se réduire à une espace, alors qu'un <p> est une vraie frontière.
    private const char BlockSeparator = '';

    // Les blocs qui produisent un saut de ligne à la fermeture : sans eux, tout le texte se
    // retrouverait collé en un seul paragraphe.
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "br", "li", "ul", "ol", "h1", "h2", "h3", "h4", "h5", "h6",
        "tr", "table", "blockquote", "pre", "hr", "section", "article", "header", "footer",
    };

    // Le contenu de ces balises n'est pas du texte destiné au lecteur : il est jeté entièrement.
    private static readonly HashSet<string> SkippedContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "iframe", "noscript",
    };

    /// <summary>
    /// Transforme du HTML en texte plat : balises retirées, entités décodées, blocs séparés par
    /// des sauts de ligne, espaces consécutifs réduits. Une entrée vide ou nulle donne une chaîne
    /// vide.
    /// </summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(html.Length);
        var index = 0;
        string? skipUntilClosing = null;

        while (index < html.Length)
        {
            var character = html[index];

            if (character != '<')
            {
                if (skipUntilClosing is null)
                {
                    builder.Append(char.IsControl(character) ? ' ' : character);
                }

                index++;
                continue;
            }

            var end = html.IndexOf('>', index);
            if (end < 0)
            {
                // Un '<' orphelin en fin de chaîne : c'est du texte, pas une balise tronquée.
                if (skipUntilClosing is null)
                {
                    builder.Append(html.AsSpan(index).ToString().Replace('\n', ' ').Replace('\r', ' '));
                }

                break;
            }

            var tagName = ReadTagName(html, index + 1, end);
            var isClosing = index + 1 < html.Length && html[index + 1] == '/';

            if (skipUntilClosing is not null)
            {
                if (isClosing && string.Equals(tagName, skipUntilClosing, StringComparison.OrdinalIgnoreCase))
                {
                    skipUntilClosing = null;
                }
            }
            else if (!isClosing && SkippedContentTags.Contains(tagName))
            {
                skipUntilClosing = tagName;
            }
            else if (BlockTags.Contains(tagName))
            {
                builder.Append(BlockSeparator);
            }

            index = end + 1;
        }

        return Normalize(WebUtility.HtmlDecode(builder.ToString()));
    }

    /// <summary>
    /// Texte plat tronqué à <paramref name="maxLength"/> caractères, coupé sur une frontière de
    /// mot et suivi d'une ellipse quand il a fallu couper. Sert au résumé affiché dans la fiche.
    /// </summary>
    public static string ToSummary(string? html, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);

        var text = ToPlainText(html).ReplaceLineEndings(" ");
        text = CollapseSpaces(text);

        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', maxLength);

        return string.Concat(text.AsSpan(0, cut > maxLength / 2 ? cut : maxLength).TrimEnd(), "…");
    }

    /// <summary>
    /// Décode les entités HTML d'une chaîne sans toucher au reste. Utile pour les champs courts
    /// (<c>summary</c>, <c>name</c>) qui ne portent pas de balises mais peuvent porter des
    /// entités.
    /// </summary>
    public static string DecodeEntities(string? value)
        => string.IsNullOrEmpty(value) || value.IndexOf('&') < 0 ? value ?? string.Empty : WebUtility.HtmlDecode(value);

    // Nom de balise : les caractères jusqu'au premier séparateur, '/' de fermeture exclu.
    private static string ReadTagName(string html, int start, int end)
    {
        var index = start;
        if (index < end && html[index] == '/')
        {
            index++;
        }

        var nameStart = index;
        while (index < end && char.IsLetterOrDigit(html[index]))
        {
            index++;
        }

        return html[nameStart..index];
    }

    // Sans normalisation, le texte extrait serait truffé de lignes vides et d'espaces en cascade.
    private static string Normalize(string text)
    {
        var lines = text
            .Split(BlockSeparator)
            .Select(line => CollapseSpaces(line).Trim())
            .Where(line => line.Length > 0);

        return string.Join("\n", lines);
    }

    private static string CollapseSpaces(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;

        foreach (var character in value)
        {
            var isSpace = char.IsWhiteSpace(character);
            if (isSpace && previousWasSpace)
            {
                continue;
            }

            builder.Append(isSpace ? ' ' : character);
            previousWasSpace = isSpace;
        }

        return builder.ToString();
    }
}