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