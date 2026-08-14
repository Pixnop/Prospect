using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Settings;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.FirstRun;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Migration;
using Prospect.Desktop.ViewModels.Settings;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.Views.Settings;

using Shouldly;

namespace Prospect.Desktop.Tests.Journeys;

/// <summary>
/// PARCOURS 5 — faire le tour des réglages. Thème et fond changent SOUS LES YEUX ; la langue, non,
/// et l'écran doit le dire au lieu de laisser croire à un bouton mort ; le retour au premier
/// lancement rouvre bien l'écran d'accueil ; et l'import VS Launcher part d'ici comme du premier
/// lancement.
/// </summary>
/// <remarks>
/// La persistance de la langue est vérifiée par RELECTURE (un second
/// <see cref="SettingsService.LoadAsync"/> sur le même disque factice), parce que c'est la seule
/// preuve qui vaille pour un réglage qui ne s'applique qu'au démarrage suivant : sans elle, un
/// sélecteur qui n'écrirait rien serait indiscernable d'un sélecteur qui marche.
/// </remarks>
public sealed class SettingsJourneyTests
{
    [AvaloniaFact]
    public async Task Journey_ThemeBackdropLanguageReplayAndAdoption_EachSaysWhatItDid()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out _, out _);
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/survie", "1.20.4");
        provider.SeedInstalledVersion(fileSystem, "1.20.4");

        var settingsService = provider.GetRequiredService<SettingsService>();
        await settingsService.LoadAsync();
        provider.GetRequiredService<ThemeService>().ApplyStartupTheme();

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();

        // ── Étape 1 : atteindre les réglages par la barre latérale ──────────────────────
        shell.SettingsNavItem.SelectCommand.Execute(null);
        window.Pump();

        var settings = shell.CurrentPage.ShouldBeOfType<SettingsViewModel>();
        window.GetVisualDescendants().OfType<SettingsView>().ShouldNotBeEmpty();
        settings.SelectedTab.ShouldBe(SettingsTab.General);

        // ── Étape 2 : le thème s'applique à chaud ───────────────────────────────────────
        var initialVariant = window.ActualThemeVariant;
        settings.General.SelectThemeCommand.Execute(ThemePreference.Light);
        window.Pump();

        window.ActualThemeVariant.ShouldBe(ThemeVariant.Light);
        window.ActualThemeVariant.ShouldNotBe(initialVariant);
        settingsService.Current.Theme.ShouldBe(ThemePreference.Light);

        settings.General.SelectThemeCommand.Execute(ThemePreference.Dark);
        window.Pump();
        window.ActualThemeVariant.ShouldBe(ThemeVariant.Dark);

        // ── Étape 3 : le fond aussi ─────────────────────────────────────────────────────
        var backdrop = provider.GetRequiredService<BackdropService>();
        var firstKey = backdrop.Key;
        var otherBackdrop = settings.General.BackdropChoices.First(choice => choice.Key != settings.General.SelectedBackdropKey);
        otherBackdrop.SelectCommand.Execute(null);
        window.Pump();

        settings.General.SelectedBackdropKey.ShouldBe(otherBackdrop.Key);
        otherBackdrop.IsSelected.ShouldBeTrue("la vignette choisie doit se marquer comme telle");
        backdrop.Key.ShouldBe(otherBackdrop.Key);
        backdrop.Key.ShouldNotBe(firstKey, "le fond doit changer sans redémarrer");
        settingsService.Current.Backdrop.ShouldBe(otherBackdrop.Key);

        // ── Étape 4 : la langue, elle, attend le prochain démarrage — et l'écran le dit ──
        // Le rang de départ n'est PAS supposé : une installation neuve déduit sa langue de la
        // culture d'interface du système (voir IUiCulture), donc il vaut français ici et anglais
        // sur une CI en en-US. Ce que le parcours vérifie est le basculement vers l'AUTRE langue.
        var startingIndex = settings.General.SelectedLanguageIndex;
        var otherIndex = startingIndex == 0 ? 1 : 0;
        var otherLanguage = otherIndex == 1 ? ProspectSettings.English : ProspectSettings.French;

        settings.General.SelectedLanguageIndex = otherIndex;
        window.Pump();

        settings.General.SelectedLanguage.ShouldBe(otherLanguage);
        window.ShowsText(JourneyHarness.ResourceText("Settings_LanguageRestartHint")).ShouldBeTrue(
            "un réglage qui ne s'applique pas tout de suite doit le dire, sinon il passe pour cassé");

        // Persistance vérifiée par relecture : un second service, le même disque.
        var reloaded = new SettingsService(
            fileSystem,
            provider.GetRequiredService<Prospect.Core.Storage.AppPaths>(),
            provider.GetRequiredService<Prospect.Core.Storage.JsonFileStore>(),
            provider.GetRequiredService<Prospect.Core.Settings.Migrations.SettingsMigrationPipeline>(),
            provider.GetRequiredService<Prospect.Core.Common.IUiCulture>());
        await reloaded.LoadAsync();
        reloaded.Current.Language.ShouldBe(otherLanguage);
        reloaded.Current.Theme.ShouldBe(ThemePreference.Dark);
        reloaded.Current.Backdrop.ShouldBe(otherBackdrop.Key);

        // ── Étape 5 : revoir le premier lancement ───────────────────────────────────────
        await settings.ReplayFirstRunCommand.ExecuteAsync(null);
        window.Pump();

        var firstRun = shell.Overlay.Active.ShouldBeOfType<FirstRunScreenViewModel>();
        firstRun.Steps.ShouldNotBeEmpty();
        firstRun.SkipCommand.Execute(null);
        window.Pump();
        shell.Overlay.Active.ShouldBeNull();

        // ── Étape 6 : l'import VS Launcher, depuis les réglages ─────────────────────────
        await settings.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        settings.VslDetected.ShouldBeTrue("un dossier VS Launcher est posé : la détection doit le voir");
        settings.VslStatusText.ShouldNotBeNullOrWhiteSpace("la détection doit RÉSUMER ce qu'elle a trouvé");
        settings.OpenAdoptionCommand.CanExecute(null).ShouldBeTrue();
        window.ShowsText(JourneyHarness.ResourceText("Settings_VslCardDescription")).ShouldBeTrue(
            "la carte doit dire que l'import COPIE avant qu'on ouvre le dialogue pour l'apprendre");

        settings.OpenAdoptionCommand.Execute(null);
        window.Pump();

        var adopt = shell.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();
        adopt.HasInstallations.ShouldBeTrue();

        // Le nom de l'écran promet une copie : l'écran doit tenir la promesse par écrit, à l'étape
        // où la décision se prend. C'est la question de quelqu'un qui a encore ses parties en cours
        // sous VS Launcher, et un mot dans un bouton n'y répond pas tout seul.
        window.ShowsText(JourneyHarness.ResourceText("Dialog_AdoptVsl_Title")).ShouldBeTrue();
        window.ShowsText(JourneyHarness.ResourceText("Dialog_AdoptVsl_CopyNotice")).ShouldBeTrue(
            "l'écran d'import doit dire noir sur blanc que le dossier VS Launcher n'est pas touché");
        window.HasEnabledButton(JourneyHarness.ResourceText("Dialog_AdoptVsl_Confirm")).ShouldBeTrue();

        await adopt.ConfirmCommand.ExecuteAsync(null);
        window.Pump();

        adopt.ReportGroups.ShouldNotBeEmpty("un import doit rendre compte de ce qu'il a fait, ligne par ligne");
        home.Instances.Select(instance => instance.Name).ShouldContain("Survie médiévale");

        // Et le dossier source est toujours là, intact : c'est la seule preuve qui vaille pour une
        // promesse de non-destruction, un texte à l'écran n'en est pas une.
        fileSystem.Directory.Exists("/vsl/installations/survie").ShouldBeTrue(
            "importer COPIE : le dossier VS Launcher d'origine ne bouge pas");

        window.Close();
    }
}