using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Prospect.Core.ModDb;
using Prospect.Desktop.Controls;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Mods;

using Shouldly;

namespace Prospect.Desktop.Tests.Mods;

/// <summary>
/// Le RENDU d'une description de fiche ModDB : la moitié « contrôles » du dispositif dont
/// <c>HtmlRichTextParserTests</c> couvre la moitié « calcul ».
/// </summary>
/// <remarks>
/// Ces tests montent un <see cref="RichTextPresenter"/> dans une vraie fenêtre headless avec le
/// moteur Skia (voir <c>TestAppBuilder</c>) : ce qu'ils vérifient est ce qui est réellement
/// composé et mesuré, pas un modèle intermédiaire.
/// </remarks>
public sealed class RichTextRenderingTests
{
    private static readonly Uri Target = new("https://example.invalid/doc");

    private sealed record Rendered(Window Window, RichTextPresenter Presenter, RichTextDocumentViewModel Document, FakeExternalUrlOpener Opener);

    private static Rendered Render(string html, IModLogoCacheOverride? images = null, double width = 640)
    {
        var opener = new FakeExternalUrlOpener();
        var document = new RichTextDocumentViewModel(
            HtmlRichTextParser.Parse(html),
            opener,
            images?.Cache ?? ModDbDoubles.CreateLogoCache(),
            imageWidth: 640);

        var presenter = new RichTextPresenter { Document = document, Width = width };
        var window = new Window { Content = presenter, Width = width + 40, Height = 600 };
        window.Show();
        window.Settle();

        return new Rendered(window, presenter, document, opener);
    }

    /// <summary>Petit sac pour passer un cache d'images observable sans alourdir la signature.</summary>
    internal sealed record IModLogoCacheOverride(Prospect.Desktop.Services.IModLogoCache Cache);

    private static T[] Descendants<T>(Visual root)
        where T : Visual
        => root.GetVisualDescendants().OfType<T>().ToArray();

    // ── Blocs ────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Render_EmptyDocument_ProducesNothingAtAll()
    {
        var rendered = Render("   ");

        rendered.Document.IsEmpty.ShouldBeTrue();
        rendered.Presenter.Child.ShouldBeNull();

        rendered.Window.Close();
    }

    [AvaloniaFact]
    public void Render_HeadingAndParagraph_KeepTheirOwnLook()
    {
        var rendered = Render("<h2>Titre</h2><p>Corps</p>");

        var texts = Descendants<TextBlock>(rendered.Presenter);
        var heading = texts.Single(text => text.Classes.Contains("richHeading"));
        var body = texts.Single(text => text.Classes.Contains("richBody"));

        heading.Classes.ShouldContain("richHeading2");
        // Un titre doit se voir COMME un titre : plus gros que le corps, sinon le rendu n'apporte
        // rien de plus que le texte aplati qu'il remplace.
        heading.FontSize.ShouldBeGreaterThan(body.FontSize);

        rendered.Window.Close();
    }

    [AvaloniaFact]
    public void Render_CharacterMarkup_BecomesInlineFormatting()
    {
        var rendered = Render("<p>normal <strong>gras</strong> <em>italique</em> <s>barré</s></p>");

        var runs = Descendants<TextBlock>(rendered.Presenter).Single().Inlines!.OfType<Run>().ToArray();

        runs.Single(run => run.Text?.Trim() == "gras").FontWeight.ShouldBe(FontWeight.SemiBold);
        runs.Single(run => run.Text?.Trim() == "italique").FontStyle.ShouldBe(FontStyle.Italic);
        runs.Single(run => run.Text?.Trim() == "barré").TextDecorations!
            .ShouldContain(decoration => decoration.Location == TextDecorationLocation.Strikethrough);

        rendered.Window.Close();
    }

    [AvaloniaFact]
    public void Render_NestedList_IndentsItsChildrenUnderTheirItem()
    {
        var rendered = Render("<ul><li>parent<ul><li>enfant</li></ul></li></ul>");

        var texts = Descendants<TextBlock>(rendered.Presenter);
        var bullets = texts.Where(text => text.Classes.Contains("richBullet")).ToArray();
        bullets.Length.ShouldBe(2);

        var parent = texts.Single(text => text.Text == "parent" || text.Inlines?.Text == "parent");
        var child = texts.Single(text => text.Text == "enfant" || text.Inlines?.Text == "enfant");

        var parentLeft = parent.TranslatePoint(default, rendered.Presenter)!.Value.X;
        var childLeft = child.TranslatePoint(default, rendered.Presenter)!.Value.X;
        childLeft.ShouldBeGreaterThan(parentLeft);

        rendered.Window.Close();
    }

    [AvaloniaFact]
    public void Render_OrderedList_NumbersItsItems()
    {
        var rendered = Render("<ol><li>un</li><li>deux</li><li>trois</li></ol>");

        Descendants<TextBlock>(rendered.Presenter)
            .Where(text => text.Classes.Contains("richBullet"))
            .Select(text => text.Text)
            .ShouldBe(["1.", "2.", "3."]);

        rendered.Window.Close();
    }

    [AvaloniaFact]
    public void Render_PreformattedBlock_UsesTheMonospacedFamilyAndWraps()
    {
        var rendered = Render("<pre>{\n  \"modid\": \"carryon\"\n}</pre>");

        var code = Descendants<TextBlock>(rendered.Presenter).Single(text => text.Classes.Contains("richCodeText"));

        code.Text!.ShouldContain("\"modid\"");
        // Enroulé, jamais rogné : la garde de mise en page exige que tout texte trop large soit
        // explicitement autorisé à passer à la ligne ou à se tronquer.
        code.TextWrapping.ShouldBe(TextWrapping.Wrap);

        rendered.Window.Close();
    }

    // ── Liens ────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Render_Link_IsUnderlinedAccentedAndClickable()
    {
        var rendered = Render($"""<p>voir <a href="{Target}">la doc</a></p>""");

        var paragraph = Descendants<TextBlock>(rendered.Presenter).Single();
        var link = paragraph.Inlines!.OfType<Run>().Single(run => run.Text?.Trim() == "la doc");

        link.TextDecorations!.ShouldContain(decoration => decoration.Location == TextDecorationLocation.Underline);
        var accent = link.Foreground.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;
        Application.Current!.TryGetResource("AccentText", ThemeVariant.Dark, out var expected).ShouldBeTrue();
        accent.ShouldBe(expected.ShouldBeOfType<SolidColorBrush>().Color);

        rendered.Window.Close();
    }

    [AvaloniaFact]
    public void Render_LinkColour_FollowsTheThemeInsteadOfStayingPinnedToTheDarkOne()
    {
        // Un Run n'est pas un Control : il ne bénéficie ni des sélecteurs de style ni de
        // DynamicResource, donc sa couleur doit être REPOSÉE à chaque bascule. C'est exactement la
        // classe de défaut qu'avait trouvée la QA du thème clair (des couleurs figées en sombre
        // sur un fond devenu clair).
        var rendered = Render($"""<p><a href="{Target}">la doc</a></p>""");
        var link = Descendants<TextBlock>(rendered.Presenter).Single().Inlines!.OfType<Run>().Single();
        var dark = link.Foreground.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        rendered.Window.Settle();

        var light = link.Foreground.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;
        light.ShouldNotBe(dark);

        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        rendered.Window.Close();
    }

    [AvaloniaFact]
    public void Render_ClickOnALink_OpensItExternallyAndNeverNavigatesInside()
    {
        var rendered = Render($"""<p><a href="{Target}">la doc</a></p>""");
        var paragraph = Descendants<LinkableTextBlock>(rendered.Presenter).Single();

        paragraph.HasLinks.ShouldBeTrue();
        // Le fond transparent n'est pas décoratif : sans lui, un TextBlock ne reçoit aucun
        // évènement de pointeur et le lien serait dessiné mais mort.
        paragraph.Background.ShouldBe(Brushes.Transparent);

        // Hit-test au premier caractère du texte, là où le lien commence.
        paragraph.FindLink(new Point(2, paragraph.Bounds.Height / 2)).ShouldBe(Target);
        rendered.Document.OpenLink(Target);

        rendered.Opener.Opened.ShouldHaveSingleItem().ShouldBe(Target);

        rendered.Window.Close();
    }

    [AvaloniaFact]
    public void Render_TextOutsideAnyLink_IsNotClickable()
    {
        var rendered = Render($"""<p>avant <a href="{Target}">la doc</a></p>""");
        var paragraph = Descendants<LinkableTextBlock>(rendered.Presenter).Single();

        paragraph.FindLink(new Point(2, paragraph.Bounds.Height / 2)).ShouldBeNull();

        rendered.Window.Close();
    }

    [AvaloniaFact]
    public void Render_ParagraphWithoutLinks_StaysTransparentToThePointer()
    {
        var rendered = Render("<p>rien à cliquer</p>");

        Descendants<LinkableTextBlock>(rendered.Presenter).Single().Background.ShouldBeNull();

        rendered.Window.Close();
    }

    // ── Images ───────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Render_Image_IsFetchedAtTheReadingColumnWidthAndNotAtTheCdnResolution()
    {
        // La discipline de ModLogoCache appliquée à une illustration : elle est décodée à la
        // largeur d'USAGE. Garder la résolution du CDN est ce qui avait fait monter le jeu de
        // travail à près de 8 Gio sur le catalogue réel.
        var cache = new FakeModLogoCache(new Avalonia.Media.Imaging.Bitmap(new MemoryStream(TinyPng.Create(size: 64))));
        var rendered = Render(
            """<p><img src="https://vintagestory-carryon.s3.us-east-1.amazonaws.com/coffee.png" alt="Coffee"></p>""",
            new IModLogoCacheOverride(cache));

        await rendered.Document.Images.Single().LoadCompletion;
        rendered.Window.Settle();

        cache.CallCount.ShouldBe(1);
        cache.LastMaxWidth.ShouldBe(640);
        cache.LastUrl!.Host.ShouldEndWith("amazonaws.com");

        var picture = Descendants<Image>(rendered.Presenter).Single();
        picture.IsVisible.ShouldBeTrue();
        picture.Source.ShouldNotBeNull();
        picture.MaxWidth.ShouldBe(640);
        picture.StretchDirection.ShouldBe(StretchDirection.DownOnly);

        rendered.Window.Close();
    }

    [AvaloniaFact]
    public async Task Render_ImageThatNeverArrives_LeavesItsAlternateTextRatherThanAHole()
    {
        var rendered = Render(
            """<p><img src="https://example.invalid/absent.png" alt="Une bannière"></p>""",
            new IModLogoCacheOverride(new FakeModLogoCache(result: null)));

        await rendered.Document.Images.Single().LoadCompletion;
        rendered.Window.Settle();

        Descendants<Image>(rendered.Presenter).Single().IsVisible.ShouldBeFalse();
        var fallback = Descendants<TextBlock>(rendered.Presenter).Single(text => text.Classes.Contains("richImageFallback"));
        fallback.IsVisible.ShouldBeTrue();
        fallback.Text.ShouldBe("Une bannière");

        rendered.Window.Close();
    }

    [AvaloniaFact]
    public void Dispose_CancelsTheImageLoadsWithoutTouchingTheBitmaps()
    {
        // Même règle absolue que pour les vignettes de cartes : le bitmap appartient au cache, qui
        // peut l'avoir distribué ailleurs. Le libérer ferait lever la passe de mise en page
        // suivante — un plantage réel, déjà corrigé une fois.
        var cache = new FakeModLogoCache(hangUntilCanceled: true);
        var rendered = Render(
            """<p><img src="https://example.invalid/a.png"></p>""",
            new IModLogoCacheOverride(cache));

        cache.LastCancellationToken.IsCancellationRequested.ShouldBeFalse();
        rendered.Document.Dispose();

        cache.LastCancellationToken.IsCancellationRequested.ShouldBeTrue();

        rendered.Window.Close();
    }

    // ── Fiche réelle ─────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Render_TheRealCarryOnDescription_ComposesEveryKindOfBlock()
    {
        var rendered = Render(RealModDbSamples.CarryOnDescriptionHtml);

        var texts = Descendants<TextBlock>(rendered.Presenter);
        texts.Count(text => text.Classes.Contains("richHeading")).ShouldBeGreaterThan(20);
        texts.Count(text => text.Classes.Contains("richBullet")).ShouldBeGreaterThan(100);
        Descendants<Border>(rendered.Presenter).ShouldContain(border => border.Classes.Contains("richCode"));
        Descendants<Image>(rendered.Presenter).Length.ShouldBe(4);

        // Les 34 liens de la fiche réelle se répartissent sur une quinzaine de blocs : chacun de
        // ces blocs doit être RÉELLEMENT cliquable, pas seulement coloré en accent.
        Descendants<LinkableTextBlock>(rendered.Presenter).Count(text => text.HasLinks).ShouldBeGreaterThan(10);

        rendered.Window.Close();
    }
}