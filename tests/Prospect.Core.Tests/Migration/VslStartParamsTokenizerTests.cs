using Prospect.Core.Migration;

using Shouldly;

namespace Prospect.Core.Tests.Migration;

public class VslStartParamsTokenizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Tokenize_BlankInput_ReturnsEmptyList(string? input)
    {
        VslStartParamsTokenizer.Tokenize(input).ShouldBeEmpty();
    }

    [Fact]
    public void Tokenize_SingleFlag_ReturnsOneToken()
    {
        VslStartParamsTokenizer.Tokenize("-logexcept").ShouldBe(["-logexcept"]);
    }

    [Fact]
    public void Tokenize_MultipleFlagsSeparatedBySpaces_ReturnsOneTokenEach()
    {
        VslStartParamsTokenizer.Tokenize("-logexcept -consoleForceEnglish -disableWatchDog")
            .ShouldBe(["-logexcept", "-consoleForceEnglish", "-disableWatchDog"]);
    }

    [Fact]
    public void Tokenize_ConsecutiveSpaces_AreCollapsed()
    {
        VslStartParamsTokenizer.Tokenize("-a    -b").ShouldBe(["-a", "-b"]);
    }

    [Fact]
    public void Tokenize_LeadingAndTrailingSpaces_AreTrimmed()
    {
        VslStartParamsTokenizer.Tokenize("  -a -b  ").ShouldBe(["-a", "-b"]);
    }

    [Fact]
    public void Tokenize_DoubleQuotedValueWithSpaces_StaysOneToken()
    {
        VslStartParamsTokenizer.Tokenize("""-playerName "John Doe" -verbose""")
            .ShouldBe(["-playerName", "John Doe", "-verbose"]);
    }

    [Fact]
    public void Tokenize_SingleQuotedValueWithSpaces_StaysOneToken()
    {
        VslStartParamsTokenizer.Tokenize("-playerName 'Jane Doe'").ShouldBe(["-playerName", "Jane Doe"]);
    }

    [Fact]
    public void Tokenize_FlagWithEqualsAndQuotedValue_KeepsThemAsOneToken()
    {
        // Forme documentée par le wiki des paramètres de démarrage : --dataPath=<chemin>, sans
        // espace autour du "=", donc un seul token attendu même si la valeur contient des espaces.
        VslStartParamsTokenizer.Tokenize("""--dataPath="C:\Users\Jane Doe\VintagestoryData" """)
            .ShouldBe(["""--dataPath=C:\Users\Jane Doe\VintagestoryData"""]);
    }

    [Fact]
    public void Tokenize_UnterminatedQuote_ToleratesAndReturnsCapturedText()
    {
        VslStartParamsTokenizer.Tokenize("""-playerName "John""").ShouldBe(["-playerName", "John"]);
    }

    [Fact]
    public void Tokenize_EmptyQuotedValue_IsKeptAsEmptyToken()
    {
        VslStartParamsTokenizer.Tokenize("""-tag "" -other""").ShouldBe(["-tag", string.Empty, "-other"]);
    }

    [Fact]
    public void Tokenize_SingleStringOfMultipleFlags_MatchesResearchExample()
    {
        // docs/research/vslauncher-et-distribution.md, section d : VS Launcher passe startParams
        // comme un seul élément d'argv. Une chaîne réaliste à plusieurs indicateurs doit devenir
        // autant de tokens distincts une fois passée par Prospect.
        VslStartParamsTokenizer.Tokenize("-logexcept -tracelog -dataPath=/tmp/vsdata")
            .ShouldBe(["-logexcept", "-tracelog", "-dataPath=/tmp/vsdata"]);
    }
}