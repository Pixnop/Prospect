using Avalonia.Headless.XUnit;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Toasts;

using Shouldly;

namespace Prospect.Desktop.Tests.Journeys;

/// <summary>
/// PARCOURS 2 — découverte d'un mod. De la barre latérale jusqu'à un mod réellement posé dans
/// l'instance : recherche, filtre par catégorie, ouverture de la fiche, installation avec sa
/// dépendance cochée, retour au navigateur qui montre le mod comme installé, et onglet Mods de
/// l'instance qui liste les DEUX archives.
/// </summary>
/// <remarks>
/// Le point que ce parcours garde et qu'aucun test d'écran ne couvrait : la BOUCLE. Le navigateur
/// et l'onglet Mods lisent le même dossier par deux chemins différents, et rien ne vérifiait que
/// ce qui vient d'être installé depuis l'un se voit immédiatement dans l'autre, sans quitter
/// l'écran ni recharger la page.
/// </remarks>
public sealed class ModDiscoveryJourneyTests
{
    [AvaloniaFact]
    public async Task Journey_BrowseSearchFilterInstallWithDependency_ShowsUpOnBothScreens()
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

        // ── Étape 1 : le navigateur, atteint par la barre latérale ────────────────────────
        shell.LibraryNavItems.Single(item => ReferenceEquals(item.Page, shell.ModBrowser)).SelectCommand.Execute(null);
        var browser = shell.CurrentPage.ShouldBeOfType<ModBrowserViewModel>();
        await browser.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        browser.IsOffline.ShouldBeFalse();
        browser.Results.ShouldNotBeEmpty("le navigateur doit s'ouvrir sur du contenu, pas sur un choix à faire");
        browser.SelectedInstance?.Slug.ShouldBe(slug, "une instance réelle doit être préchoisie comme cible d'installation");

        // ── Étape 2 : recherche ──────────────────────────────────────────────────────────
        browser.SearchText = "carry";
        window.Pump();
        browser.Results.Select(card => card.Name).ShouldContain("Carry On");
        browser.Results.Select(card => card.Name).ShouldNotContain("BetterRuins");

        // ── Étape 3 : filtre par catégorie ───────────────────────────────────────────────
        browser.SearchText = string.Empty;
        window.Pump();
        var utility = browser.Tags.Single(tag => tag.Name == "Utility");
        await browser.ToggleTagCommand.ExecuteAsync(utility);
        window.Pump();

        utility.IsActive.ShouldBeTrue("une catégorie active doit se voir, sinon on ne sait pas pourquoi la liste a changé");
        browser.Results.Select(card => card.Name).ShouldContain("Carry On");
        browser.Results.Select(card => card.Name).ShouldNotContain("BetterRuins");

        // ── Étape 4 : la fiche ───────────────────────────────────────────────────────────
        var card = browser.Results.Single(entry => entry.Name == "Carry On");
        await card.OpenCommand.ExecuteAsync(null);
        window.Pump();

        var detail = shell.Overlay.Active.ShouldBeOfType<ModDetailDialogViewModel>();
        detail.Name.ShouldBe("Carry On");

        // Le résumé d'une ligne suit la carte jusque dans la fiche : sans lui, savoir à quoi sert
        // le mod demandait d'entamer une description qui fait ici 29 Ko. Il vient du catalogue déjà
        // chargé, donc la fiche ne redemande rien au réseau pour l'afficher.
        detail.HasSummary.ShouldBeTrue("la fiche doit dire à quoi sert le mod avant sa description");
        detail.Summary.ShouldBe(card.Description, "c'est le résumé de la carte, pas un second texte");
        window.ShowsText(detail.Summary).ShouldBeTrue("un résumé non rendu ne sert à personne");

        detail.HasDescription.ShouldBeTrue("une fiche sans description ne dit pas à quoi sert le mod");
        detail.Description.IsEmpty.ShouldBeFalse("la description HTML doit être rendue, pas affichée en balises");
        detail.CanInstall.ShouldBeTrue("la fiche doit porter l'action principale, sans obliger à refermer pour installer");
        window.HasEnabledButton(JourneyHarness.ResourceText("Mods_Install")).ShouldBeTrue();

        // ── Étape 5 : installation, dépendance comprise ──────────────────────────────────
        await detail.InstallCommand.ExecuteAsync(null);
        window.Pump();

        var plan = shell.Overlay.Active.ShouldBeOfType<ModInstallPlanDialogViewModel>();
        plan.HasDependencies.ShouldBeTrue("CarryOnLib est déclarée par l'archive : le plan doit la proposer");
        var dependency = plan.Dependencies.ShouldHaveSingleItem();
        dependency.DisplayName.ShouldBe("CarryOnLib");
        dependency.IsSelected.ShouldBeTrue("une dépendance manquante est cochée d'avance : la décocher est le geste rare");

        await plan.ConfirmCommand.ExecuteAsync(null);
        window.Pump();

        shell.Overlay.Active.ShouldBeNull("le plan se referme une fois appliqué");
        var toast = toasts.ShouldHaveToast(ToastTone.Success);
        toast.Description.ShouldNotBeNullOrWhiteSpace("le succès doit dire COMBIEN de mods sont entrés et où");

        // ── Étape 6 : retour au navigateur, la carte s'est mise à jour toute seule ───────
        card.IsInstalled.ShouldBeTrue("la carte doit refléter ce qui vient d'entrer dans l'instance, sans recharger l'écran");
        card.InstalledText.ShouldNotBeNullOrWhiteSpace();

        // ── Étape 7 : l'onglet Mods de l'instance liste les deux archives ────────────────
        shell.ShowInstanceDetail(slug);
        var instance = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await instance.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        instance.ModsTab.HasMods.ShouldBeTrue();
        instance.ModsTab.Mods.Select(row => row.Name).ShouldContain("Carry On");
        instance.ModsTab.Mods.Select(row => row.Name).ShouldContain("CarryOnLib");
        instance.ModsTab.SummaryText.ShouldNotBeNullOrWhiteSpace("la liste doit se résumer d'une phrase, pas se compter à la main");

        window.Close();
    }
}