using Prospect.Core.ModDb;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

/// <summary>
/// Le sous-ensemble HTML que Prospect promet de rendre, balise par balise, puis confronté à la
/// description réelle de Carry On (29 Ko d'éditeur WYSIWYG, voir <see cref="RichTextFixtures"/>).
/// </summary>
public sealed class HtmlRichTextParserTests
{
    private static IReadOnlyList<RichTextBlock> Parse(string html) => HtmlRichTextParser.Parse(html).Blocks;

    private static string TextOf(IEnumerable<RichTextRun> runs)
        => string.Concat(runs.Select(run => run.IsLineBreak ? "\n" : run.Text));

    private static string TextOf(RichTextBlock block) => block switch
    {
        RichTextParagraph paragraph => TextOf(paragraph.Runs),
        RichTextHeading heading => TextOf(heading.Runs),
        RichTextCodeBlock code => code.Text,
        _ => string.Empty,
    };

    // ── Entrées dégénérées ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void Parse_EmptyInput_GivesTheEmptyDocument(string? html)
        => HtmlRichTextParser.Parse(html).IsEmpty.ShouldBeTrue();

    [Fact]
    public void Parse_PlainTextWithoutAnyMarkup_GivesASingleParagraph()
        => Parse("Un mod qui porte des coffres.")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextParagraph>()
            .Runs.ShouldHaveSingleItem()
            .Text.ShouldBe("Un mod qui porte des coffres.");

    // ── Blocs ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Paragraphs_BecomeSeparateBlocks()
    {
        var blocks = Parse("<p>Premier</p><p>Second</p>");

        blocks.Count.ShouldBe(2);
        blocks.ShouldAllBe(block => block is RichTextParagraph);
        blocks.Select(TextOf).ShouldBe(["Premier", "Second"]);
    }

    [Theory]
    [InlineData("h1", 1)]
    [InlineData("h2", 2)]
    [InlineData("h3", 3)]
    [InlineData("h4", 4)]
    [InlineData("h5", 5)]
    [InlineData("h6", 6)]
    public void Parse_Headings_KeepTheirLevel(string tag, int level)
    {
        var heading = Parse($"<{tag}>Titre</{tag}>").ShouldHaveSingleItem().ShouldBeOfType<RichTextHeading>();

        heading.Level.ShouldBe(level);
        TextOf(heading.Runs).ShouldBe("Titre");
    }

    [Fact]
    public void Parse_LineBreak_CutsInsideTheBlockWithoutStartingANewOne()
    {
        var paragraph = Parse("<p>Avant<br>Après</p>").ShouldHaveSingleItem().ShouldBeOfType<RichTextParagraph>();

        paragraph.Runs.Count.ShouldBe(3);
        paragraph.Runs[1].IsLineBreak.ShouldBeTrue();
        TextOf(paragraph.Runs).ShouldBe("Avant\nAprès");
    }

    [Fact]
    public void Parse_HorizontalRule_BecomesItsOwnBlock()
        => Parse("<p>a</p><hr><p>b</p>")[1].ShouldBeOfType<RichTextRule>();

    [Fact]
    public void Parse_SourceIndentation_CollapsesToSingleSpaces()
        => TextOf(Parse("<p>trop     d'espaces\n\n\tici</p>")[0]).ShouldBe("trop d'espaces ici");

    [Fact]
    public void Parse_NonBreakingSpaceSpacerParagraph_IsDropped()
    {
        // Le motif <p>&nbsp;</p> sert d'espaceur sur les vraies fiches (trois occurrences rien que
        // dans l'en-tête de Carry On) : le garder produirait des paragraphes vides à l'écran.
        var blocks = Parse("<p>Avant</p><p>&nbsp;</p><p>Après</p>");

        blocks.Count.ShouldBe(2);
        blocks.Select(TextOf).ShouldBe(["Avant", "Après"]);
    }

    // ── Enrichissements de caractère ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("strong", RichTextStyle.Bold)]
    [InlineData("b", RichTextStyle.Bold)]
    [InlineData("em", RichTextStyle.Italic)]
    [InlineData("i", RichTextStyle.Italic)]
    [InlineData("u", RichTextStyle.Underline)]
    [InlineData("s", RichTextStyle.Strikethrough)]
    [InlineData("del", RichTextStyle.Strikethrough)]
    [InlineData("code", RichTextStyle.Code)]
    public void Parse_CharacterMarkup_SetsItsStyle(string tag, RichTextStyle expected)
        => Parse($"<p><{tag}>mot</{tag}></p>")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextParagraph>()
            .Runs.ShouldHaveSingleItem()
            .Style.ShouldBe(expected);

    [Fact]
    public void Parse_NestedMarkup_CombinesStyles()
        => Parse("<p><strong><em>double</em></strong></p>")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextParagraph>()
            .Runs.ShouldHaveSingleItem()
            .Style.ShouldBe(RichTextStyle.Bold | RichTextStyle.Italic);

    [Fact]
    public void Parse_MarkupNeverClosed_StopsAtItsBlockRatherThanBoldingTheRest()
    {
        // Les fiches réelles ne sortent pas d'un compilateur : une balise oubliée doit coûter un
        // paragraphe, pas toute la fin de la description.
        var blocks = Parse("<p><strong>gras</p><p>normal</p>");

        blocks[0].ShouldBeOfType<RichTextParagraph>().Runs[0].Style.ShouldBe(RichTextStyle.Bold);
        blocks[1].ShouldBeOfType<RichTextParagraph>().Runs[0].Style.ShouldBe(RichTextStyle.Bold);
        TextOf(blocks[1]).ShouldBe("normal");
    }

    [Fact]
    public void Parse_OrphanClosingTag_DoesNotUnderflowTheStyleCounter()
    {
        var blocks = Parse("</strong><p>normal</p><p><strong>gras</strong></p>");

        blocks[0].ShouldBeOfType<RichTextParagraph>().Runs[0].Style.ShouldBe(RichTextStyle.None);
        blocks[1].ShouldBeOfType<RichTextParagraph>().Runs[0].Style.ShouldBe(RichTextStyle.Bold);
    }

    [Fact]
    public void Parse_SpacesAroundMarkup_SurviveAsASingleSeparator()
        => TextOf(Parse("<p>avant <strong>gras</strong> après</p>")[0]).ShouldBe("avant gras après");

    // ── Liens ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Anchor_CarriesItsAbsoluteTarget()
    {
        var runs = Parse("""<p>voir <a href="https://example.invalid/doc">la doc</a></p>""")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextParagraph>()
            .Runs;

        runs[0].Link.ShouldBeNull();
        runs[1].Text.ShouldBe("la doc");
        runs[1].Link.ShouldBe(new Uri("https://example.invalid/doc"));
    }

    [Fact]
    public void Parse_AnchorWithMarkupInside_KeepsBothTheStyleAndTheTarget()
    {
        var run = Parse("""<p><a href="https://example.invalid"><strong>gras</strong></a></p>""")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextParagraph>()
            .Runs.ShouldHaveSingleItem();

        run.Style.ShouldBe(RichTextStyle.Bold);
        run.Link.ShouldBe(new Uri("https://example.invalid"));
    }

    [Theory]
    [InlineData("""<a href="/show/mod/38">relatif</a>""")]
    [InlineData("""<a href="javascript:alert(1)">script</a>""")]
    [InlineData("""<a href="">vide</a>""")]
    [InlineData("<a>sans href</a>")]
    public void Parse_AnchorWithoutAnAbsoluteHttpTarget_KeepsTheTextAndDropsTheLink(string html)
    {
        // Prospect n'a aucune navigation interne : un lien ne peut qu'être ouvert dans le
        // navigateur du système. Résoudre un chemin relatif contre le site du ModDB fabriquerait
        // une destination que l'auteur n'a pas écrite, et un schéma exotique n'a rien à faire dans
        // un IExternalUrlOpener.
        var run = Parse($"<p>{html}</p>").ShouldHaveSingleItem().ShouldBeOfType<RichTextParagraph>().Runs.ShouldHaveSingleItem();

        run.Link.ShouldBeNull();
        run.Text.ShouldNotBeEmpty();
    }

    // ── Listes ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_UnorderedList_KeepsItsItems()
    {
        var list = Parse("<ul><li>un</li><li>deux</li></ul>").ShouldHaveSingleItem().ShouldBeOfType<RichTextList>();

        list.IsOrdered.ShouldBeFalse();
        list.Items.Select(item => TextOf(item.Runs)).ShouldBe(["un", "deux"]);
    }

    [Fact]
    public void Parse_OrderedList_IsMarkedAsSuch()
        => Parse("<ol><li>un</li></ol>").ShouldHaveSingleItem().ShouldBeOfType<RichTextList>().IsOrdered.ShouldBeTrue();

    [Fact]
    public void Parse_NestedList_BecomesAChildOfItsItem()
    {
        // La forme exacte de Carry On : un <ul> ouvert DANS un <li>, avant sa fermeture.
        var list = Parse("<ul><li>parent<ul><li>enfant</li></ul></li><li>voisin</li></ul>")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextList>();

        list.Items.Count.ShouldBe(2);
        TextOf(list.Items[0].Runs).ShouldBe("parent");
        var nested = list.Items[0].Children.ShouldHaveSingleItem().ShouldBeOfType<RichTextList>();
        TextOf(nested.Items.ShouldHaveSingleItem().Runs).ShouldBe("enfant");
        TextOf(list.Items[1].Runs).ShouldBe("voisin");
    }

    [Fact]
    public void Parse_ListItemsNeverClosed_AreStillSeparated()
    {
        var list = Parse("<ul><li>un<li>deux</ul>").ShouldHaveSingleItem().ShouldBeOfType<RichTextList>();

        list.Items.Select(item => TextOf(item.Runs)).ShouldBe(["un", "deux"]);
    }

    [Fact]
    public void Parse_ListNeverClosed_IsStillEmittedAtTheEnd()
        => Parse("<ul><li>un</li>")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextList>()
            .Items.ShouldHaveSingleItem();

    // ── Code et images ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Preformatted_KeepsItsLayoutInsteadOfCollapsingIt()
        => Parse("<pre>{\n  \"modid\": \"carryon\"\n}</pre>")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextCodeBlock>()
            .Text.ShouldBe("{\n  \"modid\": \"carryon\"\n}");

    [Fact]
    public void Parse_MarkupInsidePreformatted_IsIgnoredWithoutSwallowingTheText()
        => Parse("<pre><code>dotnet build</code></pre>")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextCodeBlock>()
            .Text.ShouldBe("dotnet build");

    [Fact]
    public void Parse_Image_BecomesItsOwnBlockWithItsAlternateText()
    {
        var image = Parse("""<p><img src="https://example.invalid/a.png" alt="Une bannière"></p>""")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextImage>();

        image.Source.ShouldBe(new Uri("https://example.invalid/a.png"));
        image.AlternateText.ShouldBe("Une bannière");
        image.Link.ShouldBeNull();
    }

    [Fact]
    public void Parse_LinkedImage_CarriesTheLinkOnTheImageBlock()
        => Parse("""<p><a href="https://ko-fi.com/x"><img src="https://example.invalid/a.png"></a></p>""")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextImage>()
            .Link.ShouldBe(new Uri("https://ko-fi.com/x"));

    [Fact]
    public void Parse_ImageWithoutAUsableSource_IsDroppedRatherThanRenderedEmpty()
        => Parse("""<p>texte<img src="data:image/png;base64,AAAA"></p>""")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextParagraph>();

    [Fact]
    public void Parse_TextAroundAnImage_IsSplitIntoParagraphsAroundIt()
    {
        var blocks = Parse("""<p>avant<img src="https://example.invalid/a.png">après</p>""");

        blocks.Count.ShouldBe(3);
        TextOf(blocks[0]).ShouldBe("avant");
        blocks[1].ShouldBeOfType<RichTextImage>();
        TextOf(blocks[2]).ShouldBe("après");
    }

    // ── Dégradations ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("script")]
    [InlineData("style")]
    [InlineData("iframe")]
    [InlineData("noscript")]
    [InlineData("svg")]
    public void Parse_ExecutableOrDecorativeMarkup_IsDroppedWithItsContent(string tag)
        => Parse($"<p>avant</p><{tag}>contenu jeté</{tag}><p>après</p>")
            .Select(TextOf)
            .ShouldBe(["avant", "après"]);

    [Theory]
    [InlineData("<p>du <span class=\"x\">texte</span> stylé</p>", "du texte stylé")]
    [InlineData("<p>du <font color=\"red\">texte</font> coloré</p>", "du texte coloré")]
    [InlineData("<p>du <marquee>texte</marquee> animé</p>", "du texte animé")]
    public void Parse_UnknownMarkup_DegradesToItsText(string html, string expected)
        => TextOf(Parse(html)[0]).ShouldBe(expected);

    [Fact]
    public void Parse_Entities_AreDecoded()
        => TextOf(Parse("<p>Caf&eacute; &amp; cr&#232;me &lt;3</p>")[0]).ShouldBe("Café & crème <3");

    [Fact]
    public void Parse_UnterminatedTag_IsReadAsTextRatherThanSwallowingTheRest()
        => TextOf(Parse("<p>La version 3 < 4 pour tous")[0]).ShouldBe("La version 3 < 4 pour tous");

    [Fact]
    public void Parse_AngleBracketInsideAnAttribute_DoesNotEndTheTagEarly()
        => Parse("""<p><img src="https://example.invalid/a.png" alt="a > b"></p>""")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextImage>()
            .AlternateText.ShouldBe("a > b");

    [Fact]
    public void Parse_LookAlikeAttributeName_IsNotMistakenForTheRealOne()
        => Parse("""<p><a data-href="https://piege.invalid" href="https://vrai.invalid">x</a></p>""")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextParagraph>()
            .Runs.ShouldHaveSingleItem()
            .Link.ShouldBe(new Uri("https://vrai.invalid"));

    [Fact]
    public void Parse_HtmlComment_IsIgnoredWithoutBreakingTheTextAroundIt()
        => Parse("<p>avant<!-- une note > ici -->après</p>")
            .ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextParagraph>()
            .Runs[0].Text.ShouldBe("avantaprès");

    // ── Fiche réelle ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_TheRealCarryOnDescription_ProducesEveryKindOfBlockWithoutThrowing()
    {
        var document = HtmlRichTextParser.Parse(RichTextFixtures.CarryOnDescriptionHtml);

        document.IsEmpty.ShouldBeFalse();
        document.Blocks.OfType<RichTextParagraph>().ShouldNotBeEmpty();
        document.Blocks.OfType<RichTextHeading>().ShouldNotBeEmpty();
        document.Blocks.OfType<RichTextList>().ShouldNotBeEmpty();
        document.Blocks.OfType<RichTextCodeBlock>().ShouldNotBeEmpty();

        // Les quatre <img> de la fiche sont hébergées sur S3, pas sur le CDN du ModDB : le rendu
        // doit accepter n'importe quel hôte, l'auteur écrit l'URL qu'il veut.
        var images = document.Blocks.OfType<RichTextImage>().ToArray();
        images.Length.ShouldBe(4);
        images.ShouldContain(image => image.Source.Host.EndsWith("amazonaws.com", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_TheRealCarryOnDescription_KeepsItsOpeningSentenceAndItsFirstLink()
    {
        var blocks = HtmlRichTextParser.Parse(RichTextFixtures.CarryOnDescriptionHtml).Blocks;

        var first = blocks[0].ShouldBeOfType<RichTextParagraph>();
        TextOf(first.Runs).ShouldBe("Fork of mod made by copygirl - CarryCapacity");
        first.Runs[^1].Link.ShouldBe(new Uri("https://mods.vintagestory.at/show/mod/38"));
    }

    [Fact]
    public void Parse_TheRealCarryOnDescription_NestsItsListsAndNeverEmitsAnEmptyBlock()
    {
        var blocks = HtmlRichTextParser.Parse(RichTextFixtures.CarryOnDescriptionHtml).Blocks;

        blocks.OfType<RichTextList>()
            .SelectMany(list => list.Items)
            .ShouldContain(item => item.Children.OfType<RichTextList>().Any());

        // Un bloc sans le moindre caractère lisible est du bruit à l'écran : la fiche réelle en
        // produirait beaucoup (ses <p>&nbsp;</p>, ses <div> de mise en forme) sans le filtrage.
        blocks.OfType<RichTextParagraph>().ShouldAllBe(paragraph => paragraph.Runs.Count > 0);
        blocks.OfType<RichTextHeading>().ShouldAllBe(heading => heading.Runs.Count > 0);
        AllRuns(blocks).ShouldAllBe(run => run.IsLineBreak || run.Text.Length > 0);
    }

    [Fact]
    public void Parse_TheRealCarryOnChangelog_ReadsAsAList()
    {
        // Rien dans l'API ne dit que le changelog d'une release est du HTML : c'est le relevé
        // réel qui l'établit, et c'est pourquoi le sélecteur de version le passe par ce parseur.
        var list = HtmlRichTextParser.Parse(RichTextFixtures.CarryOnChangelogHtml)
            .Blocks.ShouldHaveSingleItem()
            .ShouldBeOfType<RichTextList>();

        list.Items.Count.ShouldBe(4);
        TextOf(list.Items[0].Runs).ShouldBe("Block trunks and collapsed chests from being attached to boats.");
    }

    private static List<RichTextRun> AllRuns(IEnumerable<RichTextBlock> blocks)
    {
        var runs = new List<RichTextRun>();
        foreach (var block in blocks)
        {
            switch (block)
            {
                case RichTextParagraph paragraph:
                    runs.AddRange(paragraph.Runs);

                    break;

                case RichTextHeading heading:
                    runs.AddRange(heading.Runs);

                    break;

                case RichTextList list:
                    foreach (var item in list.Items)
                    {
                        runs.AddRange(item.Runs);
                        runs.AddRange(AllRuns(item.Children));
                    }

                    break;

                default:
                    break;
            }
        }

        return runs;
    }
}