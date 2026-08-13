using Prospect.Core.Diagnostics;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.ModDb;
using Prospect.Core.Modpacks;
using Prospect.Core.Settings;
using Prospect.Desktop.Resources;

using Shouldly;

namespace Prospect.Desktop.Tests.Resources;

/// <summary>
/// Les textes anglais CALCULÉS : pluriels, décomptes, énumérations, formats composés. La parité des
/// membres est déjà garantie par le compilateur (<see cref="UiTextTable"/> est abstraite, une
/// langue ne peut pas oublier une phrase) ; ce qu'aucun compilateur ne vérifie, c'est qu'un
/// singulier reste au singulier et qu'une liste de trois noms se lise en anglais.
///
/// Ces tests interrogent la table anglaise DIRECTEMENT plutôt que de basculer la façade statique :
/// aucun état global n'est touché, donc ils ne peuvent pas perturber les tests qui affirment du
/// français à côté.
/// </summary>
public sealed class EnglishUiTextTests
{
    private static readonly UiTextTable English = UiText.TableFor(ProspectSettings.English);

    [Fact]
    public void TableFor_English_IsTheEnglishTable()
    {
        English.Language.ShouldBe(ProspectSettings.English);
        English.ShouldBeOfType<EnglishUiText>();
    }

    [Theory]
    [InlineData(ProspectSettings.French)]
    [InlineData("kl")]
    [InlineData(null)]
    public void TableFor_AnythingElse_FallsBackToFrench(string? language)
    {
        UiText.TableFor(language).Language.ShouldBe(ProspectSettings.French);
    }

    // ── Pluriels de décompte ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, "1 issue to fix")]
    [InlineData(4, "4 issues to fix")]
    public void Doctor_ErrorsGroupTitle_AgreesInNumber(int count, string expected)
    {
        English.Instance.Doctor.ErrorsGroupTitle(count).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1, "1 issue to watch")]
    [InlineData(2, "2 issues to watch")]
    public void Doctor_WarningsGroupTitle_AgreesInNumber(int count, string expected)
    {
        English.Instance.Doctor.WarningsGroupTitle(count).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1, "1 update")]
    [InlineData(3, "3 updates")]
    public void Home_UpdatesBadge_AgreesInNumber(int count, string expected)
    {
        English.Home.UpdatesBadge(count).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "1 update available")]
    [InlineData(7, "7 updates available")]
    public void Mods_UpdatesAvailableTitle_AgreesInNumber(int count, string expected)
    {
        English.Mods.UpdatesAvailableTitle(count).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1, 1, "1 mod installed")]
    [InlineData(1, 0, "1 mod installed, disabled")]
    [InlineData(3, 3, "3 mods installed")]
    [InlineData(3, 1, "3 mods installed · 2 disabled")]
    public void Mods_InstalledSummary_AgreesInNumber(int total, int enabled, string expected)
    {
        English.Mods.InstalledSummary(total, enabled).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1, "1 backup kept")]
    [InlineData(5, "5 backups kept")]
    public void Backups_KeepCountChoiceLabel_AgreesInNumber(int count, string expected)
    {
        English.Instance.Backups.KeepCountChoiceLabel(count).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1, "1 download at a time")]
    [InlineData(3, "3 downloads at once")]
    public void Settings_ConcurrencyChoiceLabel_AgreesInNumber(int count, string expected)
    {
        English.Settings.ConcurrencyChoiceLabel(count).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, "no mods")]
    [InlineData(1, "1 mod")]
    [InlineData(12, "12 mods")]
    public void Migration_ModCount_AgreesInNumber(int count, string expected)
    {
        English.Migration.ModCount(count).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 0, "no installs and no engines found")]
    [InlineData(1, 1, "1 install and 1 engine found")]
    [InlineData(2, 3, "2 installs and 3 engines found")]
    public void Migration_DetectionSummary_AgreesInNumberOnBothHalves(int installations, int engines, string expected)
    {
        English.Migration.DetectionSummary(installations, engines).ShouldBe(expected);
    }

    [Theory]
    [InlineData(ModpackModImportStatus.Installed, 1, "1 mod installed")]
    [InlineData(ModpackModImportStatus.Installed, 2, "2 mods installed")]
    [InlineData(ModpackModImportStatus.NotFound, 1, "1 mod not found on ModDB")]
    [InlineData(ModpackModImportStatus.VersionMissing, 2, "2 versions unavailable")]
    [InlineData(ModpackModImportStatus.Sha256Mismatch, 1, "1 checksum mismatch")]
    [InlineData(ModpackModImportStatus.Sha256Mismatch, 3, "3 checksum mismatches")]
    [InlineData(ModpackModImportStatus.NetworkFailure, 2, "2 network failures")]
    public void Modpacks_ReportGroupTitle_AgreesInNumber(ModpackModImportStatus status, int count, string expected)
    {
        English.Modpacks.ReportGroupTitle(status, count).ShouldBe(expected);
    }

    // ── Décomptes et énumérations ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(2, 0, "2 running")]
    [InlineData(0, 3, "3 queued")]
    [InlineData(2, 3, "2 running · 3 queued")]
    public void Downloads_Summary_ReadsAsEnglish(int running, int queued, string expected)
    {
        English.Downloads.Summary(running, queued).ShouldBe(expected);
    }

    [Fact]
    public void Versions_Subtitle_CountsInstalls()
    {
        English.Versions.Subtitle(0, "0 B").ShouldBe("No version installed · folder shared by every instance");
        English.Versions.Subtitle(1, "590.5 MB").ShouldBe("1 installed · 590.5 MB · folder shared by every instance");
        English.Versions.Subtitle(3, "1.70 GB").ShouldBe("3 installed · 1.70 GB · folder shared by every instance");
    }

    [Fact]
    public void Mods_ShownCount_UsesTheEnglishThousandsSeparator()
    {
        // Le séparateur de milliers change de convention avec la langue : 1 234 en français,
        // 1,234 en anglais. C'est exactement le genre de détail qu'une traduction mot à mot rate.
        English.Mods.ShownCount(60, 1234).ShouldBe("60 of 1,234 mods shown");
        English.Mods.ShownCount(1234, 1234).ShouldBe("1,234 mods");
        English.Mods.ShownCount(1, 1).ShouldBe("1 mod");
        English.Mods.ShownCount(0, 0).ShouldBe(string.Empty);
    }

    [Fact]
    public void Mods_Subtitle_UsesTheEnglishThousandsSeparator()
    {
        English.Mods.Subtitle(0).ShouldBe("Official ModDB");
        English.Mods.Subtitle(1).ShouldBe("Official ModDB · 1 mod indexed");
        English.Mods.Subtitle(5280).ShouldBe("Official ModDB · 5,280 mods indexed");
    }

    [Fact]
    public void Mods_DetailMeta_JoinsAuthorAndDownloads()
    {
        English.Mods.DetailMeta("Tyron", 12500).ShouldBe("by Tyron · 12,500 downloads");
        English.Mods.DetailMeta(" ", 3).ShouldBe("unknown author · 3 downloads");
    }

    [Fact]
    public void Mods_RowAuthor_ListsOneOrMore()
    {
        English.Mods.RowAuthor([]).ShouldBe("unknown author");
        English.Mods.RowAuthor(["Tyron"]).ShouldBe("by Tyron");
        English.Mods.RowAuthor(["Tyron", "Saraty"]).ShouldBe("by Tyron and 1 other");
        English.Mods.RowAuthor(["Tyron", "Saraty", "Milo"]).ShouldBe("by Tyron and 2 others");
    }

    [Fact]
    public void Mods_CompatibleVersions_TrimsLongLists()
    {
        English.Mods.CompatibleVersions([]).ShouldBe("no game version declared");
        English.Mods.CompatibleVersions(["1.20.4"]).ShouldBe("1.20.4");
        English.Mods.CompatibleVersions(["1.20.4", "1.21.0", "1.21.3"]).ShouldBe("1.20.4, 1.21.0, 1.21.3");
        English.Mods.CompatibleVersions(["1.20.4", "1.21.0", "1.21.3", "1.21.4", "1.21.5"])
            .ShouldBe("1.20.4, 1.21.0, 1.21.3 and 2 more");
    }

    [Fact]
    public void Mods_UninstallDependents_AgreesTheVerbWithTheList()
    {
        English.Mods.UninstallDependents([]).ShouldBe(string.Empty);
        English.Mods.UninstallDependents(["Carry capacity"])
            .ShouldBe("The mod “Carry capacity” depends on it and may stop working.");
        English.Mods.UninstallDependents(["Carry capacity", "Cartomap", "Config lib"])
            .ShouldBe("The mods “Carry capacity”, “Cartomap” and “Config lib” depend on it and may stop working.");
    }

    [Fact]
    public void Mods_UnresolvedAndDisabledDependencies_AgreeTheirPronoun()
    {
        English.Mods.DependenciesNotOnModDb(["configlib"])
            .ShouldBe("Not found on ModDB: configlib. Install it by hand if the mod turns out to need it.");
        English.Mods.DependenciesNotOnModDb(["configlib", "vsimgui"])
            .ShouldBe("Not found on ModDB: configlib, vsimgui. Install them by hand if the mod turns out to need them.");
        English.Mods.DependenciesWithoutCompatibleRelease(["CarryOnLib"], "1.22.6")
            .ShouldBe("On ModDB, but with no release for Vintage Story 1.22.6: CarryOnLib. Its author has published nothing for this game version. Those compatibility boxes are ticked by hand and fall behind, so you can still install the latest published release, knowing what you are doing.");
        English.Mods.DependenciesWithoutCompatibleRelease(["CarryOnLib", "Config lib"], "1.22.6")
            .ShouldBe("On ModDB, but with no release for Vintage Story 1.22.6: CarryOnLib, Config lib. Their authors have published nothing for this game version. Those compatibility boxes are ticked by hand and fall behind, so you can still install the latest published release, knowing what you are doing.");
        English.Mods.DisabledDependencies(["configlib"])
            .ShouldBe("There but disabled: configlib. Turn it back on from the instance's Mods tab.");
        English.Mods.DisabledDependencies(["configlib", "vsimgui"])
            .ShouldBe("There but disabled: configlib, vsimgui. Turn them back on from the instance's Mods tab.");
    }

    [Fact]
    public void Versions_UninstallDependents_AgreesTheVerbWithTheList()
    {
        English.Versions.UninstallDependents(["Homestead"])
            .ShouldBe("Instance “Homestead” uses this version and will no longer start.");
        English.Versions.UninstallDependents(["Homestead", "Sandbox"])
            .ShouldBe("Instances “Homestead” and “Sandbox” use this version and will no longer start.");
    }

    // ── Formats composés ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Dialogs_QuoteNamesTheEnglishWay()
    {
        English.Dialogs.DeleteTitle("Homestead").ShouldBe("Delete “Homestead”?");
        English.Dialogs.DuplicateSuggestedName("Homestead").ShouldBe("Homestead (copy)");
        English.Dialogs.DuplicateProgressLabel(0, 0).ShouldBe("Getting the copy ready…");
        English.Dialogs.DuplicateProgressLabel(12, 40).ShouldBe("Copying files (12/40)");
    }

    [Fact]
    public void Versions_InstalledOn_KeepsTheIsoDate()
    {
        // Format technique, pas une phrase : la date reste ISO dans les deux langues.
        var installed = new DateTimeOffset(2026, 2, 11, 9, 22, 10, TimeSpan.Zero);

        English.Versions.InstalledOn(installed).ShouldBe($"installed on {installed.ToLocalTime():yyyy-MM-dd}");
    }

    [Fact]
    public void Versions_PhaseAndBrokenReason_AreTranslated()
    {
        English.Versions.PhaseLabel(GameInstallPhase.Downloading).ShouldBe("Downloading");
        English.Versions.PhaseLabel(GameInstallPhase.Completed).ShouldBe("Done");
        English.Versions.BrokenReason(GameInstallBrokenReason.MissingCompletionMarker)
            .ShouldBe("install cut short, needs reinstalling");
    }

    [Fact]
    public void BrokenInstances_Reason_KeepsTheFileNameUntranslated()
    {
        English.BrokenInstances.Reason(InstanceBrokenReason.MissingMetadataFile).ShouldBe("instance.json missing");
        English.BrokenInstances.Reason(InstanceBrokenReason.UnsupportedSchemaVersion).ShouldBe("schema version not supported");
    }

    [Fact]
    public void Doctor_CompatibilityMessage_AgreesInNumberAndKeepsTheVersionUntouched()
    {
        English.Instance.Doctor.CompatibilityMessage(new ModCompatibilityDoctorResult(2, 1, 0, 3), "1.21.3")
            .ShouldBe("1 mod whose compatibility with 1.21.3 is not confirmed. Check for mod updates to find out more.");
        English.Instance.Doctor.CompatibilityMessage(new ModCompatibilityDoctorResult(1, 1, 1, 3), "1.21.3")
            .ShouldBe("2 mods whose compatibility with 1.21.3 is not confirmed. Check for mod updates to find out more.");
        English.Instance.Doctor.CompatibilityMessage(new ModCompatibilityDoctorResult(0, 0, 2, 2), "1.21.3")
            .ShouldBe(English.Instance.Doctor.CompatibilityUnknown);
        // Verdict sain : aucune ligne, jamais un message rassurant inutile.
        English.Instance.Doctor.CompatibilityMessage(new ModCompatibilityDoctorResult(3, 0, 0, 3), "1.21.3")
            .ShouldBe(string.Empty);
    }

    [Fact]
    public void Doctor_ModIssueMessage_ReusesTheEnglishModsReason()
    {
        // Le point de couture entre deux sections de la MÊME table : la raison vient de Mods, donc
        // une table anglaise ne doit jamais aller chercher la raison française.
        var issue = new ModDoctorIssue(
            ModDoctorIssueKind.Unidentified,
            "cartomap.zip",
            Problem: ModInfoProblem.UnreadableArchive);

        English.Instance.Doctor.ModIssueMessage(issue)
            .ShouldBe("“cartomap.zip” could not be identified (unreadable archive).");
    }

    [Fact]
    public void Modpacks_ImportedToastDescription_ReadsAsEnglish()
    {
        English.Modpacks.ImportedToastDescription(0, 0).ShouldBe("No mod in this pack.");
        English.Modpacks.ImportedToastDescription(4, 4).ShouldBe("4/4 mods installed.");
        English.Modpacks.ImportedToastDescription(2, 4).ShouldBe("2/4 mods installed, see the report for the rest.");
    }

    [Fact]
    public void Modpacks_ImportGameVersionWarning_MentionsTheSizeOnlyWhenThereIsOne()
    {
        English.Modpacks.ImportGameVersionWarning("1.21.3", string.Empty)
            .ShouldBe("Game version 1.21.3 is not installed. It will be downloaded before the import.");
        English.Modpacks.ImportGameVersionWarning("1.21.3", "590.5 MB")
            .ShouldBe("Game version 1.21.3 is not installed: 590.5 MB to download before the import.");
    }

    [Fact]
    public void Migration_CompletedToastDescription_CoversEveryCombination()
    {
        English.Migration.CompletedToastDescription(0, 0).ShouldBe("Nothing was adopted, see the report.");
        English.Migration.CompletedToastDescription(1, 0).ShouldBe("1 instance created.");
        English.Migration.CompletedToastDescription(2, 0).ShouldBe("2 instances created.");
        English.Migration.CompletedToastDescription(0, 1).ShouldBe("1 engine adopted.");
        English.Migration.CompletedToastDescription(2, 3).ShouldBe("2 instance(s) created, 3 engine(s) adopted.");
    }

    [Fact]
    public void FirstRun_InstalledVersionsSummary_AgreesInNumber()
    {
        English.FirstRun.InstalledVersionsSummary(1, "1.21.3").ShouldBe("1.21.3 installed");
        English.FirstRun.InstalledVersionsSummary(3, "1.21.3").ShouldBe("3 versions installed, 1.21.3 among them");
    }

    // ── Temps humain ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Time_RelativeExpressions_AreEnglish()
    {
        English.Time.Never.ShouldBe("never");
        English.Time.Today.ShouldBe("today");
        English.Time.Yesterday.ShouldBe("yesterday");
        English.Time.DaysAgo(4).ShouldBe("4 days ago");
    }

    [Fact]
    public void Time_AbsoluteDate_FollowsTheEnglishOrder()
    {
        English.Time.AbsoluteDate(new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc)).ShouldBe("August 13, 2026");
    }

    [Fact]
    public void Time_Playtime_ReadsAsEnglish()
    {
        English.Time.NeverPlayed.ShouldBe("never played");
        English.Time.PlayedUnderAnHour.ShouldBe("played < 1 h");
        English.Time.PlayedHours(128).ShouldBe("played 128 h");
    }

    // ── Ce qui ne se traduit PAS ─────────────────────────────────────────────────────────────

    [Fact]
    public void LanguageNeutralFormats_AreIdenticalInBothTables()
    {
        var french = UiText.TableFor(ProspectSettings.French);
        var created = new DateTimeOffset(2026, 2, 11, 9, 22, 10, TimeSpan.Zero);

        English.Toasts.WithVersion("Homestead", "1.21.3").ShouldBe(french.Toasts.WithVersion("Homestead", "1.21.3"));
        English.Mods.InstanceLabel("Homestead", "1.21.3").ShouldBe(french.Mods.InstanceLabel("Homestead", "1.21.3"));
        English.Mods.ReleaseDate(created).ShouldBe(french.Mods.ReleaseDate(created));
        English.Mods.ProvenanceModDb.ShouldBe("ModDB");
        English.Versions.DownloadDetail("230.1 MB / 590.5 MB", "4.2 MB/s")
            .ShouldBe(french.Versions.DownloadDetail("230.1 MB / 590.5 MB", "4.2 MB/s"));
        English.Mods.BulkUpdateProgress(1, 4, "configlib").ShouldBe(french.Mods.BulkUpdateProgress(1, 4, "configlib"));
    }

    /// <summary>
    /// Le détail de la phase d'installation porte un mot, donc il se traduit — et le pourcentage
    /// suit la convention typographique de chaque langue : le français met une espace avant le
    /// signe, l'anglais non. C'est exactement le genre d'écart qu'on rate en traduisant.
    /// </summary>
    [Fact]
    public void Versions_InstallDetail_FollowsEachLanguagePercentConvention()
    {
        var french = UiText.TableFor(ProspectSettings.French);

        English.Versions.InstallDetail(42).ShouldBe("extracting · 42%");
        french.Versions.InstallDetail(42).ShouldBe("extraction · 42 %");
    }
}