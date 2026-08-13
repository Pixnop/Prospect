using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;

using Prospect.Desktop.Services;

using Shouldly;

namespace Prospect.Desktop.Tests.Resources;

/// <summary>
/// La garde de parité des deux dictionnaires de textes (<c>Strings.fr.axaml</c> et
/// <c>Strings.en.axaml</c>). Une clé ajoutée d'un seul côté ne se voit pas à la compilation : un
/// <c>{StaticResource}</c> se résout à l'exécution, donc une vue chercherait une ressource absente
/// et la fenêtre s'ouvrirait cassée dans une langue et pas dans l'autre. Ce test la nomme.
/// </summary>
public sealed class StringsDictionaryParityTests
{
    [AvaloniaFact]
    public void BothDictionaries_CarryExactlyTheSameKeys()
    {
        var french = LoadKeys(LanguageService.FrenchDictionaryUri);
        var english = LoadKeys(LanguageService.EnglishDictionaryUri);

        var missingFromEnglish = french.Except(english, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var missingFromFrench = english.Except(french, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        missingFromEnglish.ShouldBeEmpty($"Clés absentes de Strings.en.axaml : {string.Join(", ", missingFromEnglish)}");
        missingFromFrench.ShouldBeEmpty($"Clés absentes de Strings.fr.axaml : {string.Join(", ", missingFromFrench)}");
    }

    [AvaloniaFact]
    public void BothDictionaries_AreNotEmpty()
    {
        // Garde-fou de la garde elle-même : si le chargement rendait un dictionnaire vide, la
        // comparaison de clés ci-dessus passerait sans rien prouver.
        LoadKeys(LanguageService.FrenchDictionaryUri).Length.ShouldBeGreaterThan(200);
        LoadKeys(LanguageService.EnglishDictionaryUri).Length.ShouldBeGreaterThan(200);
    }

    [AvaloniaTheory]
    [InlineData(LanguageService.FrenchDictionaryUri)]
    [InlineData(LanguageService.EnglishDictionaryUri)]
    public void EveryValue_IsANonEmptyString(string dictionaryUri)
    {
        var dictionary = Load(dictionaryUri);

        var empty = dictionary
            .Where(entry => entry.Value is not string text || string.IsNullOrWhiteSpace(text))
            .Select(entry => entry.Key.ToString() ?? "?")
            .Order(StringComparer.Ordinal)
            .ToArray();

        empty.ShouldBeEmpty($"Clés sans texte exploitable dans {dictionaryUri} : {string.Join(", ", empty)}");
    }

    private static IResourceDictionary Load(string dictionaryUri)
    {
        var uri = new Uri(dictionaryUri);

        return new ResourceInclude(uri) { Source = uri }.Loaded;
    }

    private static string[] LoadKeys(string dictionaryUri)
        => Load(dictionaryUri).Keys.Select(key => key.ToString() ?? "?").ToArray();
}