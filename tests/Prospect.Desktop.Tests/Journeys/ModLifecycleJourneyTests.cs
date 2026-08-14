using Avalonia.Headless.XUnit;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.ModDb;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Toasts;

using Shouldly;

namespace Prospect.Desktop.Tests.Journeys;

/// <summary>
/// PARCOURS 3 — cycle de vie d'un mod, du jour de son installation à son retrait. Choisir une
/// AUTRE version que celle proposée (rétrograder), désactiver sans supprimer, réactiver, demander
/// une vérification de mises à jour et LIRE son verdict, tout mettre à jour, puis désinstaller un
/// mod dont un autre dépend et voir l'avertissement nommer le mod cassé.
/// </summary>
/// <remarks>
/// Ce parcours a été écrit pour attraper les silences. Un bouton « Vérifier les mises à jour » qui
/// ne trouve rien ne change RIEN à l'écran s'il n'affiche pas de verdict, et une désactivation qui
/// ne dit rien laisse croire qu'elle a échoué : les deux assertions correspondantes sont ici pour
/// que ces retours ne puissent plus disparaître.
/// </remarks>
public sealed class ModLifecycleJourneyTests
{
    [AvaloniaFact]
    public async Task Journey_DowngradeDisableReenableCheckUpdateThenUninstall_ExplainsEveryStep()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out var catalogHandler, out _);
        catalogHandler.ModDb.CatalogJson = FakeModDbHandler.CatalogWith(FakeModDbHandler.CarryOnCatalogEntry);
        catalogHandler.ModDb.CompatibleModIds = [1783, 792, 890, 4687];
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var slug = await provider.SeedTargetInstanceAsync("Homestead", "1.21.3");

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var toasts = provider.GetRequiredService<IToastService>();
        window.Show();

        shell.ShowModBrowser(slug);
        var browser = shell.CurrentPage.ShouldBeOfType<ModBrowserViewModel>();
        await browser.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        // ── Étape 1 : installer, mais PAS la version proposée ────────────────────────────
        var card = browser.Results.Single(entry => entry.Name == "Carry On");
        await card.InstallCommand.ExecuteAsync(null);
        window.Pump();

        var plan = shell.Overlay.Active.ShouldBeOfType<ModInstallPlanDialogViewModel>();
        plan.HasReleaseChoice.ShouldBeTrue("plusieurs versions compatibles existent : le choix doit être offert");
        plan.SelectedRelease!.VersionText.ShouldBe("1.14.3", "la plus récente compatible est présélectionnée");

        var older = plan.Releases.Single(release => release.VersionText == "1.14.2");
        plan.SelectedRelease = older;
        window.Pump();

        plan.Plan.Primary.Version.ToString().ShouldBe("1.14.2", "changer de version doit recalculer le plan, pas seulement l'étiquette");
        plan.FileNameText.ShouldContain("1.14.2", Case.Insensitive);
        plan.HasDependencies.ShouldBeFalse("cette version-là ne déclare aucune dépendance : le plan doit le refléter");

        await plan.ConfirmCommand.ExecuteAsync(null);
        window.Pump();
        toasts.ShouldHaveToast(ToastTone.Success);

        // ── Étape 2 : l'onglet Mods de l'instance ────────────────────────────────────────
        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        var row = detail.ModsTab.Mods.ShouldHaveSingleItem();
        row.Name.ShouldBe("Carry On");
        row.VersionText.ShouldBe("1.14.2");
        row.IsEnabled.ShouldBeTrue();

        // ── Étape 3 : désactiver puis réactiver, chaque fois avec un retour visible ──────
        row.IsEnabled = false;
        window.Pump();
        toasts.LastToast().ShouldNotBeNull().Title.ShouldNotBeNullOrWhiteSpace("désactiver un mod doit le dire");
        detail.ModsTab.Mods.ShouldHaveSingleItem().IsEnabled.ShouldBeFalse();
        detail.ModsTab.SummaryText.ShouldNotBeNullOrWhiteSpace();

        var disabledRow = detail.ModsTab.Mods[0];
        disabledRow.IsEnabled = true;
        window.Pump();
        detail.ModsTab.Mods.ShouldHaveSingleItem().IsEnabled.ShouldBeTrue();

        // ── Étape 4 : vérifier les mises à jour, et LIRE le verdict ──────────────────────
        detail.ModsTab.CheckVerdictText.ShouldBeEmpty("avant toute vérification, rien n'est affirmé");

        await detail.ModsTab.CheckUpdatesCommand.ExecuteAsync(null);
        window.Pump();

        detail.ModsTab.HasCheckVerdict.ShouldBeTrue(
            "une vérification qui ne trouve rien doit quand même dire qu'elle a eu lieu, sinon le bouton passe pour cassé");
        detail.ModsTab.CheckVerdictText.ShouldNotBeNullOrWhiteSpace();
        detail.ModsTab.LastCheckedText.ShouldNotBeNullOrWhiteSpace();
        window.ShowsText(detail.ModsTab.CheckVerdictText).ShouldBeTrue("le verdict doit être RENDU, pas seulement calculé");

        // ── Étape 5 : une mise à jour existe, « Tout mettre à jour » l'applique ──────────
        catalogHandler.ModDb.UpdatesJson = """
        {
          "statuscode": "200",
          "updates": {
            "carryon": {
              "releaseid": 55101, "fileid": 118001, "mainfile": "https://moddbcdn.vintagestory.at/carryon_1.14.3.zip",
              "filename": "carryon_1.14.3.zip", "downloads": 900, "tags": ["1.21.3"], "modidstr": "carryon",
              "modversion": "1.14.3", "changelog": null, "created": "2026-08-07 10:22:05"
            }
          }
        }
        """;

        await detail.ModsTab.CheckUpdatesCommand.ExecuteAsync(null);
        window.Pump();

        detail.ModsTab.AvailableUpdateCount.ShouldBe(1);
        detail.ModsTab.HasAvailableUpdates.ShouldBeTrue();
        window.ShowsText(detail.ModsTab.UpdatesAvailableTitle).ShouldBeTrue("le bandeau des mises à jour doit être visible");
        detail.ModsTab.UpdateAllCommand.CanExecute(null).ShouldBeTrue();

        await detail.ModsTab.UpdateAllCommand.ExecuteAsync(null);
        window.Pump();

        toasts.ShouldHaveToast(ToastTone.Success);
        detail.ModsTab.Mods.ShouldHaveSingleItem().VersionText.ShouldBe("1.14.3");
        detail.ModsTab.AvailableUpdateCount.ShouldBe(0, "les badges d'un état devenu faux disparaissent");

        // ── Étape 6 : désinstaller un mod dont un autre dépend ───────────────────────────
        // La 1.14.3 réclame CarryOnLib ; on le pose pour que le retrait de la bibliothèque ait un
        // dépendant réel à nommer.
        var mods = provider.GetRequiredService<IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(slug),
            "carryonlib-1.2.0.zip",
            ModDbDoubles.ModInfo("carryonlib", "CarryOnLib", "1.2.0"));

        await detail.ModsTab.RefreshCommand.ExecuteAsync(null);
        window.Pump();
        detail.ModsTab.Mods.Count.ShouldBe(2);

        var library = detail.ModsTab.Mods.Single(entry => entry.Name == "CarryOnLib");
        await library.RemoveCommand.ExecuteAsync(null);
        window.Pump();

        var uninstall = shell.Overlay.Active.ShouldBeOfType<UninstallModDialogViewModel>();
        uninstall.HasDependents.ShouldBeTrue("retirer une bibliothèque dont un mod dépend doit prévenir");
        uninstall.DependentsMessage.ShouldContain("Carry On", Case.Insensitive, "l'avertissement doit NOMMER le mod cassé");
        window.ShowsText("Carry On").ShouldBeTrue();

        await uninstall.ConfirmCommand.ExecuteAsync(null);
        window.Pump();

        shell.Overlay.Active.ShouldBeNull();
        toasts.LastToast().ShouldNotBeNull().Title.ShouldNotBeNullOrWhiteSpace("un retrait doit le dire");
        detail.ModsTab.Mods.ShouldHaveSingleItem().Name.ShouldBe("Carry On");

        window.Close();
    }
}