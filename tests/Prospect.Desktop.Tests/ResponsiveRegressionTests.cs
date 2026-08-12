using Avalonia.Headless.XUnit;
using Avalonia.Styling;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.GameVersions;
using Prospect.Core.Modpacks;
using Prospect.Core.Storage;

using Prospect.Desktop.Tests.Support;
using Prospect.Desktop.ViewModels.FirstRun;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Migration;
using Prospect.Desktop.ViewModels.Modpacks;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Settings;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Toasts;
using Prospect.Desktop.ViewModels.Versions;
using Prospect.Desktop.ViewModels.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests;

/// <summary>
/// La garde de mise en page : chaque écran et chaque état significatif du launcher, arpenté par
/// <see cref="LayoutInvariantWalker"/> au plancher de la fenêtre (960x600), à 1100x700 et à la
/// taille de référence du design (1280x800). Zéro violation exigée.
///
/// Née d'un retour d'usage sans appel après une session de test réelle : « plein d'endroits où des
/// textes dépassent, des chevauchements ». Ces défauts ne se voient pas à la taille de la maquette,
/// seulement quand la fenêtre rétrécit — et personne ne redimensionne l'application avant chaque
/// commit. La garde, elle, le fait, sur tous les écrans, à chaque exécution de la suite.
///
/// Le harnais suit le reste des tests headless : conteneur DI de production, système de fichiers en
/// mémoire, réseau factice. Aucune donnée n'est inventée côté vue, tout passe par les ViewModels
/// réels, sinon la garde vérifierait une mise en page que personne n'affiche.
/// </summary>
public sealed class ResponsiveRegressionTests
{
    [AvaloniaFact]
    public void TheWindowFloor_IsExactlyTheSmallestSizeTheGuardChecks()
    {
        // Le lien entre les deux moitiés du dispositif : la fenêtre refuse de descendre sous une
        // taille, la garde vérifie la mise en page à cette taille-là. Abaisser l'une sans étendre
        // l'autre ouvrirait une zone où plus rien n'est garanti — ce test le rend impossible en
        // silence.
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);

        window.MinWidth.ShouldBe(ResponsiveWindowSizes.Floor.Width);
        window.MinHeight.ShouldBe(ResponsiveWindowSizes.Floor.Height);

        window.Close();
    }

    // ── Accueil ──────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Home_Empty_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var home = provider.GetRequiredService<HomeViewModel>();

        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();
        home.HasNoInstancesAtAll.ShouldBeTrue();

        window.ShouldHoldLayoutInvariantsAtEverySize("Accueil, aucune instance");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Home_Populated_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.20.4");
        await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.ShortInstanceName, "1.20.4");
        window.Settle();
        home.Instances.Count.ShouldBe(2);

        window.ShouldHoldLayoutInvariantsAtEverySize("Accueil peuplé");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Home_Populated_HoldsItsBoxesInTheLightTheme()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider, ThemeVariant.Light);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.20.4");
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize("Accueil peuplé, thème clair");

        window.Close();
    }

    // ── Détail d'instance ────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task InstanceDetail_EveryTab_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.20.4");
        ResponsiveScenario.SeedWorldsAndJournal(provider, fileSystem, record.Slug);
        ResponsiveScenario.SeedInstalledMod(provider, fileSystem, record.Slug);

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Settle();
        detail.HasWorlds.ShouldBeTrue();
        detail.HasJournalContent.ShouldBeTrue();

        foreach (var tab in Enum.GetValues<InstanceDetailTab>())
        {
            detail.SelectTabCommand.Execute(tab);
            window.Settle();
            window.ShouldHoldLayoutInvariantsAtEverySize($"Détail d'instance, onglet {tab}");
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task InstanceDetail_HoldsItsBoxesInTheLightTheme()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider, ThemeVariant.Light);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.20.4");
        ResponsiveScenario.SeedWorldsAndJournal(provider, fileSystem, record.Slug);
        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize("Détail d'instance, thème clair");

        window.Close();
    }

    [AvaloniaFact]
    public async Task InstanceDetail_LaunchErrorBanner_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.20.4");
        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);

        // Le fichier sentinelle de l'installation disparaît : la version redevient « absente » pour
        // le launcher, qui échoue AVANT de démarrer le moindre processus (GameLauncher valide dans
        // cet ordre). Le bandeau d'erreur s'affiche donc, avec son action « Installer », sans que ce
        // test ait à toucher au vrai SystemProcessRunner du conteneur de production.
        fileSystem.RemoveFile(fileSystem.Path.Combine(
            provider.GetRequiredService<AppPaths>().VersionsDirectory,
            "1.20.4",
            FileSystemInstalledGameVersionRepository.CompletionMarkerFileName));
        await detail.PlayCommand.ExecuteAsync(null);
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize("Détail d'instance, bandeau d'erreur de lancement");

        window.Close();
    }

    // ── Navigateur de mods ───────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task ModBrowser_Populated_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();
        shell.ModBrowser.Results.ShouldNotBeEmpty();

        window.ShouldHoldLayoutInvariantsAtEverySize("Navigateur de mods peuplé");

        // Recherche active : la croix d'effacement apparaît DANS le champ, l'une des rares
        // superpositions déclarées du produit, et la grille se réduit à un résultat.
        shell.ModBrowser.SearchText = "config";
        window.Settle();
        shell.ModBrowser.Results.ShouldNotBeEmpty();

        window.ShouldHoldLayoutInvariantsAtEverySize("Navigateur de mods, recherche active");

        window.Close();
    }

    [AvaloniaFact]
    public async Task ModDetailDialog_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        await shell.ModBrowser.Results[0].OpenCommand.ExecuteAsync(null);
        window.Settle();
        shell.Overlay.Active.ShouldBeOfType<ModDetailDialogViewModel>();

        window.ShouldHoldLayoutInvariantsAtEverySize("Fiche de mod");

        window.Close();
    }

    [AvaloniaFact]
    public async Task ModUpdatePlanDialog_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out var catalogHandler);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.21.3");
        ResponsiveScenario.SeedOutdatedMod(provider, fileSystem, catalogHandler, record.Slug);

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        await detail.ModsTab.CheckUpdatesCommand.ExecuteAsync(null);
        window.Settle();

        await detail.ModsTab.Mods.ShouldHaveSingleItem().UpdateCommand.ExecuteAsync(null);
        window.Settle();
        shell.Overlay.Active.ShouldBeOfType<ModUpdatePlanDialogViewModel>();

        window.ShouldHoldLayoutInvariantsAtEverySize("Dialogue de plan de mise à jour");

        window.Close();
    }

    [AvaloniaFact]
    public async Task ModBrowser_Offline_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out var catalogHandler);
        catalogHandler.ModDb.IsOnline = false;
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();
        shell.ModBrowser.IsOffline.ShouldBeTrue();

        window.ShouldHoldLayoutInvariantsAtEverySize("Navigateur de mods hors ligne");

        window.Close();
    }

    // ── Versions ─────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Versions_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var versions = provider.GetRequiredService<VersionsViewModel>();

        shell.LibraryNavItems.First(item => item.Label == "Versions").SelectCommand.Execute(null);
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Settle();
        versions.Installed.ShouldNotBeEmpty();
        versions.Available.ShouldNotBeEmpty();

        window.ShouldHoldLayoutInvariantsAtEverySize("Versions");

        window.Close();
    }

    // ── Réglages et adoption VS Launcher ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Settings_NothingDetected_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        await shell.Settings.InitializeCommand.ExecuteAsync(null);
        window.Settle();
        shell.Settings.VslDetected.ShouldBeFalse();

        window.ShouldHoldLayoutInvariantsAtEverySize("Réglages, rien détecté");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Settings_VslDetected_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/survie-medievale-communautaire", "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        await shell.Settings.InitializeCommand.ExecuteAsync(null);
        window.Settle();
        shell.Settings.VslDetected.ShouldBeTrue();

        window.ShouldHoldLayoutInvariantsAtEverySize("Réglages, VS Launcher détecté");

        window.Close();
    }

    [AvaloniaFact]
    public void Settings_GeneralTab_HoldsItsBoxesInTheLightTheme()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider, ThemeVariant.Light);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize("Réglages, Général, thème clair");

        window.Close();
    }

    [AvaloniaFact]
    public void Settings_GameTab_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        shell.Settings.SelectTabCommand.Execute(SettingsTab.Game);
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize("Réglages, Jeu");

        window.Close();
    }

    [AvaloniaFact]
    public void Settings_NetworkTab_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        shell.Settings.SelectTabCommand.Execute(SettingsTab.Network);
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize("Réglages, Réseau");

        window.Close();
    }

    [AvaloniaFact]
    public void Settings_AccountsTab_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        shell.Settings.SelectTabCommand.Execute(SettingsTab.Accounts);
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize("Réglages, Comptes");

        window.Close();
    }

    [AvaloniaFact]
    public void Settings_AboutTab_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        shell.Settings.SelectTabCommand.Execute(SettingsTab.About);
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize("Réglages, À propos");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Home_EmptyWithVslDetected_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/survie-medievale-communautaire", "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var home = provider.GetRequiredService<HomeViewModel>();

        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();
        home.FirstRun.VslDetected.ShouldBeTrue();

        window.ShouldHoldLayoutInvariantsAtEverySize("Accueil vide, rappel VS Launcher");

        window.Close();
    }

    [AvaloniaFact]
    public async Task AdoptVslDialog_SelectionStep_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/" + ResponsiveScenario.LongInstanceName, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();
        home.FirstRun.OpenAdoptionCommand.Execute(null);
        window.Settle();
        shell.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();

        window.ShouldHoldLayoutInvariantsAtEverySize("Dialogue d'adoption VS Launcher, sélection");

        window.Close();
    }

    [AvaloniaFact]
    public async Task AdoptVslDialog_ReportStep_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/" + ResponsiveScenario.LongInstanceName, "1.20.4");
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();
        home.FirstRun.OpenAdoptionCommand.Execute(null);
        window.Settle();
        var adopt = shell.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();

        await adopt.ConfirmCommand.ExecuteAsync(null);
        window.Settle();
        adopt.Step.ShouldBe(AdoptVslStep.Report);

        window.ShouldHoldLayoutInvariantsAtEverySize("Dialogue d'adoption VS Launcher, rapport");

        window.Close();
    }

    // ── Écran de premier lancement ───────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void FirstRun_NothingInstalledOrDetected_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.ShowFirstRunIfNeeded();
        window.Settle();
        shell.Overlay.Active.ShouldBeOfType<FirstRunScreenViewModel>();

        window.ShouldHoldLayoutInvariantsAtEverySize("Premier lancement, aucune version ni VS Launcher");

        window.Close();
    }

    [AvaloniaFact]
    public void FirstRun_VersionInstalledAndVslDetected_HoldsItsBoxes()
    {
        // Le scénario le plus chargé de l'écran (trois lignes plutôt que deux) : celui qui tend le
        // plus la boîte de la carte à la hauteur plancher de la fenêtre (960x600).
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/" + ResponsiveScenario.LongInstanceName, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.ShowFirstRunIfNeeded();
        window.Settle();
        var firstRun = shell.Overlay.Active.ShouldBeOfType<FirstRunScreenViewModel>();
        firstRun.Steps.Count.ShouldBe(3);

        window.ShouldHoldLayoutInvariantsAtEverySize("Premier lancement, version installée et VS Launcher détecté");

        window.Close();
    }

    [AvaloniaFact]
    public void FirstRun_HoldsItsBoxesInTheLightTheme()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/" + ResponsiveScenario.LongInstanceName, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider, ThemeVariant.Light);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.ShowFirstRunIfNeeded();
        window.Settle();
        shell.Overlay.Active.ShouldBeOfType<FirstRunScreenViewModel>();

        window.ShouldHoldLayoutInvariantsAtEverySize("Premier lancement, thème clair");

        window.Close();
    }

    // ── Wizard ───────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Wizard_EveryStep_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        home.NewInstanceCommand.Execute(null);
        var wizard = shell.Overlay.Active.ShouldBeOfType<WizardViewModel>();
        await wizard.LoadVersionsCommand.ExecuteAsync(null);
        window.Settle();
        window.ShouldHoldLayoutInvariantsAtEverySize("Wizard, étape 1 (nom)");

        wizard.Name = ResponsiveScenario.LongInstanceName;
        wizard.NextCommand.Execute(null);
        window.Settle();
        wizard.IsVersionStep.ShouldBeTrue();
        window.ShouldHoldLayoutInvariantsAtEverySize("Wizard, étape 2 (version)");

        wizard.VersionChoices.First(choice => choice.VersionText == "1.20.4").SelectCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        window.Settle();
        wizard.IsIconStep.ShouldBeTrue();
        window.ShouldHoldLayoutInvariantsAtEverySize("Wizard, étape 3 (icône)");

        wizard.NextCommand.Execute(null);
        window.Settle();
        wizard.IsSummaryStep.ShouldBeTrue();
        window.ShouldHoldLayoutInvariantsAtEverySize("Wizard, étape 4 (résumé)");

        window.Close();
    }

    // ── Dialogues ────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Dialogs_HoldTheirBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.20.4");
        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        // L'import ne passe pas par HomeViewModel.ImportModpackCommand : ce chemin commence par un
        // sélecteur de fichier, qui n'existe pas en mode headless et rend null (annulation), donc
        // aucun panneau ne s'ouvrirait. Le ViewModel se construit ici par la fabrique du conteneur,
        // exactement celle qu'utilise la commande, avec une source qui n'existe pas : le dialogue
        // s'affiche alors sur son étape « échec », celle qui porte le plus long texte.
        var importFactory = provider.GetRequiredService<Func<string, ImportModpackViewModel>>();

        var dialogs = new (string Name, Action Open)[]
        {
            ("Renommer", () => detail.RenameCommand.Execute(null)),
            ("Dupliquer", () => detail.DuplicateCommand.Execute(null)),
            ("Supprimer", () => detail.DeleteCommand.Execute(null)),
            ("Exporter un modpack", () => detail.ExportCommand.Execute(null)),
            ("Importer un modpack", () =>
            {
                var import = importFactory("/import/pack-inexistant.json");
                shell.Overlay.Show(import);
                import.LoadPreviewCommand.Execute(null);
            }),
        };

        foreach (var (name, open) in dialogs)
        {
            open();
            window.Settle();
            shell.Overlay.Active.ShouldNotBeNull();
            window.ShouldHoldLayoutInvariantsAtEverySize($"Dialogue « {name} »");
            shell.Overlay.Close();
            window.Settle();
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task ImportModpackDialog_OnItsPreviewStep_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        var source = await ResponsiveScenario.WriteModpackManifestAsync(fileSystem, "Pack de survie communautaire", "1.20.4");
        var import = provider.GetRequiredService<Func<string, ImportModpackViewModel>>()(source);
        shell.Overlay.Show(import);
        await import.LoadPreviewCommand.ExecuteAsync(null);
        window.Settle();
        import.Step.ShouldBe(ImportModpackStep.Preview);

        window.ShouldHoldLayoutInvariantsAtEverySize("Dialogue d'import de modpack, aperçu");

        window.Close();
    }

    // ── Calques du shell ─────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void DownloadsPopover_Open_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.ToggleDownloadsPopoverCommand.Execute(null);
        window.Settle();
        shell.IsDownloadsPopoverOpen.ShouldBeTrue();

        window.ShouldHoldLayoutInvariantsAtEverySize("Popover Téléchargements ouvert");

        window.Close();
    }

    [AvaloniaFact]
    public void Toast_Displayed_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.Toasts.Show(
            ToastTone.Error,
            "Le téléchargement a échoué",
            "La connexion au dépôt officiel a échoué (délai dépassé après 30 s). Les mods déjà installés restent utilisables hors ligne.");
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize("Toast affiché");

        window.Close();
    }
}