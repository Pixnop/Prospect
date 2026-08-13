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
        // Le fond livré avec le thème verre : une installation qui ne touche à rien voit
        // exactement ce qu'elle voyait avant que le sélecteur n'existe.
        settings.Backdrop.ShouldBe(BackdropCatalog.Default);
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

    [Theory]
    [InlineData("village-lane", "village-lane")]
    [InlineData("VILLAGE-LANE", "village-lane")]
    [InlineData("  village-lane  ", "village-lane")]
    [InlineData(BackdropCatalog.Default, BackdropCatalog.Default)]
    [InlineData(null, BackdropCatalog.Default)]
    [InlineData("", BackdropCatalog.Default)]
    // Un douzième fond qu'un Prospect plus récent embarquerait, ou une faute de frappe : repli,
    // jamais une exception — même contrat que la langue.
    [InlineData("aurora-plateau", BackdropCatalog.Default)]
    // La clé n'est pas un chemin : le nom de fichier complet est une valeur inconnue.
    [InlineData("village-lane.jpg", BackdropCatalog.Default)]
    public void NormalizeBackdrop_FallsBackToTheDefaultForAnythingItDoesNotKnow(string? backdrop, string expected)
    {
        ProspectSettings.NormalizeBackdrop(backdrop).ShouldBe(expected);
    }

    [Fact]
    public void Normalized_UnknownBackdropFromDisk_IsRepairedToTheDefault()
    {
        var settings = ProspectSettings.CreateDefault() with { Backdrop = "nope" };

        settings.Normalized().Backdrop.ShouldBe(BackdropCatalog.Default);
    }

    [Fact]
    public void Normalized_KnownBackdrop_IsKept()
    {
        var settings = ProspectSettings.CreateDefault() with { Backdrop = "crystal-vein" };

        settings.Normalized().Backdrop.ShouldBe("crystal-vein");
    }

    [Fact]
    public void BackdropCatalog_ContainsTheDefaultAndHasNoDuplicate()
    {
        // Deux invariants que rien d'autre ne garde : le défaut doit être proposé comme les autres
        // (sinon le sélecteur n'aurait aucune entrée sélectionnée à l'ouverture), et une clé
        // dupliquée ferait deux vignettes pour le même fichier.
        BackdropCatalog.Keys.ShouldContain(BackdropCatalog.Default);
        BackdropCatalog.Keys.Distinct(StringComparer.Ordinal).Count().ShouldBe(BackdropCatalog.Keys.Count);
        BackdropCatalog.Keys.ShouldAllBe(key => key.Length > 0);
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