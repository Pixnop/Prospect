namespace Prospect.Core.ModDb;

/// <summary>
/// Enrichissements de caractère cumulables portés par un <see cref="RichTextRun"/>. Combinables :
/// l'éditeur du ModDB produit couramment du gras dans un lien, ou du code dans une puce.
/// </summary>
[Flags]
public enum RichTextStyle
{
    /// <summary>Texte courant.</summary>
    None = 0,

    /// <summary><c>&lt;strong&gt;</c> ou <c>&lt;b&gt;</c>.</summary>
    Bold = 1,

    /// <summary><c>&lt;em&gt;</c> ou <c>&lt;i&gt;</c>.</summary>
    Italic = 2,

    /// <summary><c>&lt;u&gt;</c> ou <c>&lt;ins&gt;</c>.</summary>
    Underline = 4,

    /// <summary><c>&lt;s&gt;</c>, <c>&lt;strike&gt;</c> ou <c>&lt;del&gt;</c>.</summary>
    Strikethrough = 8,

    /// <summary><c>&lt;code&gt;</c> en ligne (le <c>&lt;pre&gt;</c>, lui, devient un bloc entier).</summary>
    Code = 16,
}

/// <summary>
/// Un fragment de texte homogène : même enrichissement, même destination de lien. C'est l'unité
/// que la couche UI traduit en <c>Inline</c> Avalonia.
/// </summary>
/// <param name="Text">Texte déjà décodé de ses entités et normalisé de ses espaces.</param>
/// <param name="Style">Enrichissements cumulés au moment où ce fragment a été lu.</param>
/// <param name="Link">
/// Destination du <c>&lt;a&gt;</c> englobant, ou <see langword="null"/>. Toujours une URL
/// <c>http</c>/<c>https</c> absolue : un <c>href</c> relatif, vide ou d'un autre schéma est
/// abandonné, jamais résolu contre le site du ModDB (Prospect n'a pas de navigation interne, un
/// lien ne peut donc que partir dans le navigateur du système).
/// </param>
/// <param name="IsLineBreak">
/// Vrai pour un <c>&lt;br&gt;</c> : une coupure DANS un bloc, à ne pas confondre avec la frontière
/// entre deux blocs. La description réelle de Carry On en contient 226 pour 154 paragraphes, donc
/// les aplatir en espaces détruirait sa mise en forme.
/// </param>
public sealed record RichTextRun(
    string Text,
    RichTextStyle Style = RichTextStyle.None,
    Uri? Link = null,
    bool IsLineBreak = false)
{
    /// <summary>Le saut de ligne intra-bloc, sans texte ni style.</summary>
    public static RichTextRun LineBreak { get; } = new(string.Empty, IsLineBreak: true);
}

/// <summary>
/// Un bloc de la description : l'unité que la couche UI empile verticalement. Hiérarchie fermée
/// (toutes les variantes sont dans ce fichier), pour qu'un rendu exhaustif soit vérifiable.
/// </summary>
public abstract record RichTextBlock;

/// <summary>Un paragraphe de texte courant.</summary>
/// <param name="Runs">Fragments qui le composent, dans l'ordre de lecture.</param>
public sealed record RichTextParagraph(IReadOnlyList<RichTextRun> Runs) : RichTextBlock;

/// <summary>Un titre.</summary>
/// <param name="Level">Niveau de 1 à 6, tel que déclaré par <c>h1</c>..<c>h6</c>.</param>
/// <param name="Runs">Fragments du titre.</param>
public sealed record RichTextHeading(int Level, IReadOnlyList<RichTextRun> Runs) : RichTextBlock;

/// <summary>Une entrée de liste : son texte propre, et les blocs qu'elle contient (listes imbriquées).</summary>
/// <param name="Runs">Texte de l'entrée.</param>
/// <param name="Children">Blocs imbriqués sous cette entrée, listes filles comprises.</param>
public sealed record RichTextListItem(IReadOnlyList<RichTextRun> Runs, IReadOnlyList<RichTextBlock> Children);

/// <summary>Une liste à puces ou numérotée.</summary>
/// <param name="IsOrdered">Vrai pour <c>ol</c>, faux pour <c>ul</c>.</param>
/// <param name="Items">Entrées, dans l'ordre.</param>
public sealed record RichTextList(bool IsOrdered, IReadOnlyList<RichTextListItem> Items) : RichTextBlock;

/// <summary>Un bloc préformaté (<c>&lt;pre&gt;</c>) : espaces et retours à la ligne conservés tels quels.</summary>
/// <param name="Text">Contenu, entités décodées mais mise en forme intacte.</param>
public sealed record RichTextCodeBlock(string Text) : RichTextBlock;

/// <summary>Une séparation horizontale (<c>&lt;hr&gt;</c>).</summary>
public sealed record RichTextRule : RichTextBlock;

/// <summary>
/// Une image de la description. Sortie du flux de texte plutôt que gardée en ligne : les fiches
/// réelles les utilisent comme bannières pleine largeur (Carry On en héberge quatre sur S3, dans
/// des paragraphes qui ne contiennent qu'elles), et une image en ligne dans un
/// <c>TextBlock</c> imposerait une hauteur de ligne ingérable.
/// </summary>
/// <param name="Source">URL absolue de l'image.</param>
/// <param name="AlternateText">Texte de remplacement, vide s'il n'y en a pas.</param>
/// <param name="Link">Destination du <c>&lt;a&gt;</c> englobant, quand l'image en a un.</param>
public sealed record RichTextImage(Uri Source, string AlternateText, Uri? Link) : RichTextBlock;

/// <summary>
/// Une description entière, réduite à ses blocs. Résultat de <see cref="HtmlRichTextParser.Parse"/>.
/// </summary>
/// <param name="Blocks">Blocs, dans l'ordre du document.</param>
public sealed record RichTextDocument(IReadOnlyList<RichTextBlock> Blocks)
{
    /// <summary>Document sans le moindre bloc, rendu pour une description absente ou vide.</summary>
    public static RichTextDocument Empty { get; } = new([]);

    /// <summary>Vrai quand il n'y a rien à afficher.</summary>
    public bool IsEmpty => Blocks.Count == 0;
}
