using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Toasts;
using Prospect.Desktop.ViewModels.Versions;
using Prospect.Desktop.ViewModels.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests.Journeys;

/// <summary>
/// PARCOURS 8 — réseau coupé. Un sous-test par écran qui parle au ModDB ou au catalogue du jeu :
/// chacun doit montrer un message COMPRÉHENSIBLE et une voie de sortie, jamais un écran muet ni
/// une exception qui traverse une commande.
/// </summary>
/// <remarks>
/// <para>
/// Le gestionnaire HTTP factice lève sur TOUTE requête (<see cref="FakeCatalogHandler.IsOnline"/>
/// à faux), catalogue du jeu comme ModDB : un écran qui n'attraperait pas son échec ferait tomber
/// le sous-test qui le couvre, et un écran qui l'attraperait sans rien dire échouerait sur
/// l'assertion de message.
/// </para>
/// <para>
/// La règle que ces sous-tests figent : montrer ce qu'on a déjà (l'installé, le local), dire
/// pourquoi le reste manque, et offrir de RÉESSAYER. Un écran qui ne remplit pas les trois est un
/// écran où l'utilisateur ne peut que fermer l'application.
/// </para>
/// </remarks>
public sealed class OfflineJourneyTests
{
    [AvaloniaFact]
    public async Task Offline_ModBrowser_SaysWhyAndOffersToTryAgain()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out var catalogHandler, out _);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        await provider.SeedTargetInstanceAsync("Homestead", "1.21.3");
        catalogHandler.IsOnline = false;

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        shell.ShowModBrowser();
        var browser = shell.CurrentPage.ShouldBeOfType<ModBrowserViewModel>();
        await browser.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        browser.IsOffline.ShouldBeTrue();
        browser.ShowEmptyState.ShouldBeTrue();
        browser.EmptyStateTitle.ShouldNotBeNullOrWhiteSpace();
        browser.EmptyStateDescription.ShouldNotBeNullOrWhiteSpace("un écran vide sans explication est un écran muet");
        window.ShowsText(browser.EmptyStateTitle).ShouldBeTrue();
        window.HasEnabledButton(JourneyHarness.ResourceText("Mods_Retry")).ShouldBeTrue("hors ligne, la seule action utile doit rester à portée de clic");

        // Et la voie de sortie fonctionne réellement une fois le réseau revenu.
        catalogHandler.IsOnline = true;
        await browser.RefreshCommand.ExecuteAsync(null);
        window.Pump();

        browser.IsOffline.ShouldBeFalse();
        browser.Results.ShouldNotBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Offline_VersionsScreen_KeepsWhatIsInstalledAndSaysWhyTheRestIsMissing()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out var catalogHandler, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        catalogHandler.IsOnline = false;

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        shell.ShowVersions();
        var versions = shell.CurrentPage.ShouldBeOfType<VersionsViewModel>();
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Pump();

        versions.Installed.Select(row => row.VersionText).ShouldContain("1.20.4", "ce qui est sur le disque reste utilisable hors ligne");
        versions.Available.ShouldBeEmpty();
        versions.CatalogWarning.ShouldNotBeNullOrWhiteSpace("l'absence du catalogue doit être expliquée, pas subie");
        window.ShowsText(versions.CatalogWarning!).ShouldBeTrue();
        window.HasEnabledButton(JourneyHarness.ResourceText("Versions_Retry")).ShouldBeTrue("l'écran doit offrir de refaire l'appel qui vient d'échouer");

        catalogHandler.IsOnline = true;
        await versions.CheckForUpdatesCommand.ExecuteAsync(null);
        window.Pump();

        versions.CatalogWarning.ShouldBeNull();
        versions.Available.ShouldNotBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Offline_Wizard_StillCreatesAnInstanceFromWhatIsAlreadyInstalled()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out var catalogHandler, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        catalogHandler.IsOnline = false;

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();

        home.NewInstanceCommand.Execute(null);
        window.Pump();

        var wizard = shell.Overlay.Active.ShouldBeOfType<WizardViewModel>();
        await wizard.LoadVersionsCommand.ExecuteAsync(null);
        window.Pump();

        wizard.VersionsWarning.ShouldNotBeNullOrWhiteSpace("le wizard doit dire pourquoi il ne propose que le local");
        wizard.VersionChoices.Select(choice => choice.VersionText).ShouldContain("1.20.4");
        wizard.VersionChoices.ShouldAllBe(choice => choice.IsInstalled);

        wizard.Name = "Hors ligne";
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        await wizard.CreateCommand.ExecuteAsync(null);
        window.Pump();

        wizard.CreateError.ShouldBeNull("une version déjà installée n'a besoin d'aucun réseau");
        home.Instances.Select(instance => instance.Name).ShouldContain("Hors ligne");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Offline_InstanceModsTab_FailedUpdateCheckIsExplainedInsteadOfSilent()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out var catalogHandler, out _);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var slug = await provider.SeedTargetInstanceAsync("Homestead", "1.21.3");
        var mods = provider.GetRequiredService<Prospect.Core.ModDb.IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(slug),
            "configlib-1.11.1.zip",
            ModDbDoubles.ModInfo("configlib", "Config lib", "1.11.1"));

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var toasts = provider.GetRequiredService<IToastService>();
        window.Show();

        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        // La liste locale s'affiche : elle ne doit rien au réseau.
        detail.ModsTab.Mods.ShouldHaveSingleItem().Name.ShouldBe("Config lib");

        catalogHandler.IsOnline = false;
        await detail.ModsTab.CheckUpdatesCommand.ExecuteAsync(null);
        window.Pump();

        detail.ModsTab.IsCheckingUpdates.ShouldBeFalse("le bouton doit se rendre, pas rester bloqué");
        var toast = toasts.ShouldHaveToast(ToastTone.Error);
        toast.Title.ShouldNotBeNullOrWhiteSpace("un échec doit être annoncé, pas avalé");
        detail.ModsTab.CheckUpdatesCommand.CanExecute(null).ShouldBeTrue("et on doit pouvoir réessayer");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Offline_InstanceDoctor_StillDiagnosesAndSaysWhatItCannotKnow()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out var catalogHandler, out _);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var slug = await provider.SeedTargetInstanceAsync("Homestead", "1.21.3");
        var mods = provider.GetRequiredService<Prospect.Core.ModDb.IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(slug),
            "configlib-1.11.1.zip",
            ModDbDoubles.ModInfo("configlib", "Config lib", "1.11.1"));
        catalogHandler.IsOnline = false;

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var toasts = provider.GetRequiredService<IToastService>();
        window.Show();

        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        await detail.CheckInstanceCommand.ExecuteAsync(null);
        window.Pump();

        // Le diagnostic est LOCAL : réseau coupé, il rend quand même son verdict.
        var doctor = shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();
        var compatibility = doctor.Groups.SelectMany(group => group.Rows).Single(row => row.HasAction);
        compatibility.Message.ShouldNotBeNullOrWhiteSpace();

        // Et l'action qu'il propose, elle, a besoin du réseau : son échec doit être dit.
        compatibility.ActionCommand!.Execute(null);
        await window.WaitUntilAsync(
            () => toasts.LastToast()?.Tone == ToastTone.Error,
            "l'action du rapport a besoin du réseau : son échec doit être annoncé");

        toasts.ShouldHaveToast(ToastTone.Error).Title.ShouldNotBeNullOrWhiteSpace();
        detail.SelectedTab.ShouldBe(InstanceDetailTab.Mods, "et l'écran doit rester là où l'action a mené");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Offline_ModInstall_FromTheBrowser_FailsWithAnExplanationAndNoBrokenScreen()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out var catalogHandler, out _);
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

        var card = browser.Results.First();

        // Le réseau tombe entre le chargement du catalogue et le clic sur Installer.
        catalogHandler.IsOnline = false;
        await card.InstallCommand.ExecuteAsync(null);
        window.Pump();

        shell.Overlay.Active.ShouldBeNull("aucun plan ne doit s'ouvrir à moitié");
        card.IsBusy.ShouldBeFalse("la carte doit se rendre, pas rester en attente indéfinie");
        toasts.ShouldHaveToast(ToastTone.Error).Title.ShouldNotBeNullOrWhiteSpace();
        browser.Results.ShouldNotBeEmpty("l'écran reste utilisable");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Offline_ModDetail_FromTheBrowser_ReportsItWithoutClosingTheScreen()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out var catalogHandler, out _);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        await provider.SeedTargetInstanceAsync("Homestead", "1.21.3");

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var toasts = provider.GetRequiredService<IToastService>();
        window.Show();

        shell.ShowModBrowser();
        var browser = shell.CurrentPage.ShouldBeOfType<ModBrowserViewModel>();
        await browser.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        var card = browser.Results.First();
        catalogHandler.IsOnline = false;
        await card.OpenCommand.ExecuteAsync(null);
        window.Pump();

        shell.Overlay.Active.ShouldBeNull("une fiche qui n'a pas pu être lue ne doit pas s'ouvrir vide");
        toasts.ShouldHaveToast(ToastTone.Error).Title.ShouldNotBeNullOrWhiteSpace();
        shell.CurrentPage.ShouldBeSameAs(browser, "l'écran d'où l'on vient reste affiché");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Offline_FirstRunChecklist_StillListsItsStepsAndTheirActions()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out _, out var catalogHandler, out _);
        catalogHandler.IsOnline = false;

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        shell.ShowFirstRunIfNeeded();
        window.Pump();

        var firstRun = shell.Overlay.Active.ShouldBeOfType<Prospect.Desktop.ViewModels.FirstRun.FirstRunScreenViewModel>();
        firstRun.Steps.ShouldNotBeEmpty("la checklist est locale : elle doit rester lisible sans réseau");
        firstRun.Steps.ShouldAllBe(step => step.Title.Length > 0 && step.Subtitle.Length > 0);
        window.HasEnabledButton(JourneyHarness.ResourceText("FirstRun_StartButton")).ShouldBeTrue();

        window.Close();
    }
}