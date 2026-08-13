using System.IO.Abstractions.TestingHelpers;

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;

using Prospect.Core.Settings;
using Prospect.Core.Settings.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.Support;
using Prospect.Desktop.Tests.TestDoubles;

using Shouldly;

namespace Prospect.Desktop.Tests.Services;

/// <summary>
/// <see cref="LanguageService"/> : correspondance langue → dictionnaire, fusion effective dans
/// <see cref="Application.Resources"/>, et la garde de fixation unique de <see cref="UiText"/>.
/// Pendant de <c>ThemeServiceTests</c> pour l'autre moitié de ce qui se décide au démarrage.
/// </summary>
public sealed class LanguageServiceTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");

    private static SettingsService CreateSettings(MockFileSystem fileSystem)
        => new(fileSystem, Paths, new JsonFileStore(fileSystem), new SettingsMigrationPipeline([]), new FakeUiCulture());

    [AvaloniaFact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var settings = CreateSettings(new MockFileSystem());

        Should.Throw<ArgumentNullException>(() => new LanguageService(null!, Application.Current!));
        Should.Throw<ArgumentNullException>(() => new LanguageService(settings, null!));
    }

    [Theory]
    [InlineData(ProspectSettings.English, LanguageService.EnglishDictionaryUri)]
    [InlineData(ProspectSettings.French, LanguageService.FrenchDictionaryUri)]
    [InlineData("kl", LanguageService.FrenchDictionaryUri)]
    [InlineData(null, LanguageService.FrenchDictionaryUri)]
    public void DictionaryUriFor_FallsBackToFrenchLikeTheSettingsDo(string? language, string expected)
    {
        LanguageService.DictionaryUriFor(language).ShouldBe(expected);
    }

    [AvaloniaFact]
    public void MergeStringsDictionary_ReplacesTheDictionaryRatherThanStackingThem()
    {
        var before = CountStringsDictionaries();

        using (UiLanguageScope.Use(ProspectSettings.English))
        {
            CountStringsDictionaries().ShouldBe(before);
            CurrentStringsDictionaryUri().ShouldBe(LanguageService.EnglishDictionaryUri);
        }

        CountStringsDictionaries().ShouldBe(before);
        CurrentStringsDictionaryUri().ShouldBe(LanguageService.FrenchDictionaryUri);
    }

    [AvaloniaFact]
    public void MergeStringsDictionary_English_MakesEnglishKeysResolvable()
    {
        using (UiLanguageScope.Use(ProspectSettings.English))
        {
            Application.Current!.Resources.TryGetResource("Nav_Bibliotheque", null, out var value).ShouldBeTrue();
            value.ShouldBe("Library");
        }

        Application.Current!.Resources.TryGetResource("Nav_Bibliotheque", null, out var french).ShouldBeTrue();
        french.ShouldBe("Bibliothèque");
    }

    [AvaloniaFact]
    public void ApplyStartupLanguage_UsesThePersistedSettingForBothHalvesOfTheText()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Paths.SettingsFilePath, new MockFileData("""
        { "schemaVersion": 1, "theme": "Dark", "language": "en" }
        """));
        var settings = CreateSettings(fileSystem);
        settings.LoadAsync().GetAwaiter().GetResult();
        var service = new LanguageService(settings, Application.Current!);

        try
        {
            service.ApplyStartupLanguage();

            // Moitié XAML.
            Application.Current!.Resources.TryGetResource("Settings_Title", null, out var title).ShouldBeTrue();
            title.ShouldBe("Settings");

            // Moitié C#.
            UiText.Language.ShouldBe(ProspectSettings.English);
            UiText.Shell.NavSettings.ShouldBe("Settings");
        }
        finally
        {
            LanguageService.MergeStringsDictionary(Application.Current!, ProspectSettings.French);
            UiText.ResetForTests(ProspectSettings.French);
        }
    }

    [AvaloniaFact]
    public void ApplyStartupLanguage_CalledTwice_Throws()
    {
        // La garde de l'entorse assumée (voir la remarque d'UiText) : la langue se fixe une fois,
        // au démarrage. Un deuxième appel n'est pas un cas à rattraper, c'est un bug de câblage.
        var settings = CreateSettings(new MockFileSystem());
        var service = new LanguageService(settings, Application.Current!);

        try
        {
            service.ApplyStartupLanguage();

            var exception = Should.Throw<InvalidOperationException>(service.ApplyStartupLanguage);
            exception.Message.ShouldContain(ProspectSettings.French);
        }
        finally
        {
            UiText.ResetForTests(ProspectSettings.French);
        }
    }

    private static int CountStringsDictionaries()
        => Application.Current!.Resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .Count(include => include.Source?.OriginalString is LanguageService.FrenchDictionaryUri or LanguageService.EnglishDictionaryUri);

    private static string CurrentStringsDictionaryUri()
        => Application.Current!.Resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .Select(include => include.Source?.OriginalString)
            .Last(uri => uri is LanguageService.FrenchDictionaryUri or LanguageService.EnglishDictionaryUri)!;
}