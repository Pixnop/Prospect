using Prospect.Core.ModDb;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

public sealed class HtmlTextTests
{
    [Fact]
    public void ToPlainText_RealDescriptionSample_StripsTagsAndKeepsParagraphBreaks()
    {
        const string Html = "<h2>Config lib</h2><p>A universal place to configure your mods.</p>"
            + "<p>See the <a href=\"https://example.invalid\">docs</a>.</p>";

        HtmlText.ToPlainText(Html).ShouldBe("Config lib\nA universal place to configure your mods.\nSee the docs.");
    }

    [Fact]
    public void ToPlainText_Entities_AreDecoded()
        => HtmlText.ToPlainText("<p>Caf&eacute; &amp; cr&#232;me &lt;3</p>").ShouldBe("Café & crème <3");

    [Fact]
    public void ToPlainText_ScriptAndStyle_AreDroppedWithTheirContent()
        => HtmlText.ToPlainText("<style>p{color:red}</style><p>Texte</p><script>alert(1)</script>").ShouldBe("Texte");

    [Fact]
    public void ToPlainText_ListsAndLineBreaks_BecomeSeparateLines()
        => HtmlText.ToPlainText("<ul><li>un</li><li>deux</li></ul>Trois<br>Quatre")
            .ShouldBe("un\ndeux\nTrois\nQuatre");

    [Fact]
    public void ToPlainText_RunsOfWhitespace_AreCollapsed()
        => HtmlText.ToPlainText("<p>trop     d'espaces\n\n\tici</p>").ShouldBe("trop d'espaces ici");

    [Fact]
    public void ToPlainText_UnterminatedTag_IsTreatedAsTextRatherThanSwallowingTheRest()
        => HtmlText.ToPlainText("Version 3 < 4 pour tous").ShouldBe("Version 3 < 4 pour tous");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToPlainText_EmptyInput_GivesAnEmptyString(string? html)
        => HtmlText.ToPlainText(html).ShouldBeEmpty();

    [Fact]
    public void ToSummary_LongText_IsCutOnAWordBoundaryAndEllipsised()
    {
        var summary = HtmlText.ToSummary("<p>Prospect installe les mods du ModDB officiel sans jamais rien inventer.</p>", 30);

        summary.ShouldBe("Prospect installe les mods du…");
        summary.Length.ShouldBeLessThanOrEqualTo(31);
    }

    [Fact]
    public void ToSummary_ShortText_IsLeftAlone()
        => HtmlText.ToSummary("<p>Court</p>", 40).ShouldBe("Court");

    [Fact]
    public void ToSummary_NonPositiveLength_IsRejected()
        => Should.Throw<ArgumentOutOfRangeException>(() => HtmlText.ToSummary("<p>x</p>", 0));

    [Fact]
    public void DecodeEntities_PlainValue_IsReturnedUntouched()
        => HtmlText.DecodeEntities("Config lib").ShouldBe("Config lib");

    [Fact]
    public void DecodeEntities_EncodedValue_IsDecoded()
        => HtmlText.DecodeEntities("Travel &amp; Exploration").ShouldBe("Travel & Exploration");

    [Fact]
    public void DecodeEntities_Null_GivesAnEmptyString()
        => HtmlText.DecodeEntities(null).ShouldBeEmpty();
}