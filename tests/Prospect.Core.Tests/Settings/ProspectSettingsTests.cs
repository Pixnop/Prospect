using Prospect.Core.Settings;

using Shouldly;

namespace Prospect.Core.Tests.Settings;

public class ProspectSettingsTests
{
    [Fact]
    public void CreateDefault_HasSaneValues()
    {
        var settings = ProspectSettings.CreateDefault();

        settings.SchemaVersion.ShouldBe(ProspectSettings.CurrentSchemaVersion);
        // Dark reste le défaut : c'était déjà le comportement codé en dur d'App.axaml avant ce
        // chantier, un fichier absent ne doit pas faire basculer silencieusement le rendu.
        settings.Theme.ShouldBe(ThemePreference.Dark);
        settings.Language.ShouldBe(ProspectSettings.French);
        settings.Downloads.ShouldBe(DownloadPreferences.Default);
        // Jamais vu par défaut : une installation neuve doit voir l'écran de premier lancement.
        settings.HasSeenFirstRun.ShouldBeFalse();
    }

    [Fact]
    public void Normalized_ClampsOutOfRangeDownloadPreferences()
    {
        var settings = ProspectSettings.CreateDefault() with { Downloads = new DownloadPreferences { MaxParallelDownloads = 999 } };

        var normalized = settings.Normalized();

        normalized.Downloads.MaxParallelDownloads.ShouldBe(DownloadPreferences.MaxParallelDownloadsCeiling);
    }

    [Fact]
    public void Normalized_ValueAlreadyInRange_LeavesOtherFieldsUnchanged()
    {
        var settings = ProspectSettings.CreateDefault() with { Theme = ThemePreference.Light, Language = "fr" };

        var normalized = settings.Normalized();

        normalized.Theme.ShouldBe(ThemePreference.Light);
        normalized.Language.ShouldBe("fr");
    }

    [Theory]
    [InlineData("en", ProspectSettings.English)]
    [InlineData("EN", ProspectSettings.English)]
    [InlineData(" en ", ProspectSettings.English)]
    [InlineData("fr", ProspectSettings.French)]
    [InlineData("FR", ProspectSettings.French)]
    [InlineData(null, ProspectSettings.French)]
    [InlineData("", ProspectSettings.French)]
    // Une langue qu'un Prospect plus récent connaîtrait, ou une faute de frappe : repli, jamais
    // une exception.
    [InlineData("de", ProspectSettings.French)]
    // Un nom de culture n'est PAS la valeur stockée : la langue est l'énumération de Prospect.
    [InlineData("en-US", ProspectSettings.French)]
    public void NormalizeLanguage_FallsBackToFrenchForAnythingItDoesNotKnow(string? language, string expected)
    {
        ProspectSettings.NormalizeLanguage(language).ShouldBe(expected);
    }

    [Fact]
    public void Normalized_UnknownLanguageFromDisk_IsRepairedToFrench()
    {
        var settings = ProspectSettings.CreateDefault() with { Language = "kl" };

        settings.Normalized().Language.ShouldBe(ProspectSettings.French);
    }

    [Fact]
    public void Normalized_English_IsKept()
    {
        var settings = ProspectSettings.CreateDefault() with { Language = ProspectSettings.English };

        settings.Normalized().Language.ShouldBe(ProspectSettings.English);
    }

    [Theory]
    [InlineData("fr", ProspectSettings.French)]
    [InlineData("fr-FR", ProspectSettings.French)]
    [InlineData("fr-BE", ProspectSettings.French)]
    [InlineData("FR-fr", ProspectSettings.French)]
    [InlineData("en-GB", ProspectSettings.English)]
    [InlineData("es-ES", ProspectSettings.English)]
    [InlineData("", ProspectSettings.English)]
    [InlineData(null, ProspectSettings.English)]
    public void LanguageForCulture_MapsFrenchCulturesToFrenchAndEverythingElseToEnglish(string? cultureName, string expected)
    {
        ProspectSettings.LanguageForCulture(cultureName).ShouldBe(expected);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        // Record : deux réglages avec les mêmes valeurs doivent être égaux (utilisé par
        // SettingsServiceTests pour comparer avant/après un round-trip disque).
        var first = ProspectSettings.CreateDefault();
        var second = ProspectSettings.CreateDefault();

        first.ShouldBe(second);
    }
}