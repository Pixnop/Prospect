using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Backups;
using Prospect.Core.Common;
using Prospect.Core.Diagnostics;
using Prospect.Core.GameVersions;
using Prospect.Core.ModDb;
using Prospect.Core.Modpacks;
using Prospect.Core.Runtime;
using Prospect.Core.Storage;

using Prospect.Desktop.Layout;
using Prospect.Desktop.Tests.Support;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;
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
    public async Task Home_NewInstanceTile_HoldsItsBoxes()
    {
        // Cas dédié pour la tuile « Nouvelle instance » (dernier élément de GridItems dès qu'une
        // instance existe, voir HomeViewModel.ApplyFilterAndSort) : Home_Populated_HoldsItsBoxes
        // la mesure déjà en passant, celui-ci la nomme explicitement pour que le libellé qui
        // touchait le cadre en pointillés à certaines tailles reste un cas de non-régression lisible.
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.ShortInstanceName, "1.20.4");
        window.Settle();
        home.GridItems.ShouldContain(item => item is NewInstanceTileViewModel);

        window.ShouldHoldLayoutInvariantsAtEverySize("Accueil, tuile Nouvelle instance");

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
    public async Task InstanceDetail_ModsTabWithALongModName_HoldsItsBoxes()
    {
        // La garde générique (InstanceDetail_EveryTab_HoldsItsBoxes) ne pousse jamais le NOM d'un
        // mod : celui-ci était trimmé en apparence (TextTrimming posé) mais réellement inerte, la
        // ligne nom+badge vivant dans un StackPanel horizontal qui mesure ses enfants sans
        // contrainte de largeur — exactement le piège déjà documenté juste en dessous pour la
        // ligne auteur/fichier. Régression pour la conversion en Grid des deux lignes.
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.20.4");
        var mods = provider.GetRequiredService<IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(record.Slug),
            "cartomap-1.0.0.zip",
            ModDbDoubles.ModInfo("cartomap", "Extension de cartographie détaillée pour la région nord du monde"));

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Settle();
        detail.ModsTab.Mods.ShouldHaveSingleItem().Name.ShouldContain("cartographie");

        window.ShouldHoldLayoutInvariantsAtEverySize("Détail d'instance, onglet Mods, nom de mod long");

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

    [AvaloniaFact]
    public async Task InstanceDetail_OptionsTabWithPopulatedBackups_HoldsItsBoxes()
    {
        // La garde générique (InstanceDetail_EveryTab_HoldsItsBoxes) visite l'onglet Options avec
        // le bloc Sauvegardes VIDE (aucune sauvegarde à la création) ; celle-ci couvre l'état
        // peuplé (plusieurs lignes, boutons Restaurer/Supprimer), jamais mesuré ailleurs.
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.20.4");
        var backups = provider.GetRequiredService<InstanceBackupService>();
        await backups.CreateAsync(record.Slug, progress: null, CancellationToken.None);
        await backups.CreateAsync(record.Slug, progress: null, CancellationToken.None);

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        detail.SelectTabCommand.Execute(InstanceDetailTab.Options);
        window.Settle();
        detail.OptionsTab.Backups.HasBackups.ShouldBeTrue();
        detail.OptionsTab.Backups.Backups.Count.ShouldBe(2);

        window.ShouldHoldLayoutInvariantsAtEverySize("Détail d'instance, Options, sauvegardes peuplées");

        window.Close();
    }

    [AvaloniaFact]
    public async Task InstanceDoctorDialog_AllClear_HoldsItsBoxes()
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

        // État « tout va bien » construit directement : l'obtenir en environnement de test
        // demanderait un vrai runtimeconfig.json (donc un vrai `dotnet --list-runtimes`) et un
        // volume factice configuré, deux détails de diagnostic déjà couverts côté Core
        // (InstanceDoctorTests) qui n'ont rien à apporter à une garde de mise en page.
        var report = new InstanceDoctorReport(
            new GameVersionDoctorResult(GameVersionDoctorStatus.Installed, GameVersion.Parse("1.20.4")),
            RuntimeCheckResult.Present(GameRuntimeRequirement.Known("Microsoft.NETCore.App", new Version(8, 0, 10))),
            [],
            new ModCompatibilityDoctorResult(0, 0, 0, 0),
            new DiskSpaceDoctorResult(100L * 1024 * 1024 * 1024, InstanceDoctor.LowDiskSpaceThresholdBytes));
        shell.Overlay.Show(new InstanceDoctorDialogViewModel(report, () => { }, () => { }, shell.Overlay));
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize("Dialogue du docteur d'instance, tout va bien");

        window.Close();
    }

    [AvaloniaFact]
    public async Task InstanceDoctorDialog_WithFindings_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.20.4");
        var mods = provider.GetRequiredService<IInstalledModRepository>();
        // Identifiant de dépendance volontairement long (visible uniquement dans la ligne du
        // docteur, pas dans la liste de l'onglet Mods) : c'est ce genre de valeur, pas
        // « vsimgui », qui pousse une ligne de rapport contre le bord du dialogue.
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(record.Slug),
            "cartomap-1.0.0.zip",
            ModDbDoubles.ModInfo(
                "cartomap",
                "Cartographie détaillée",
                dependency: "un-mod-de-base-manquant-au-nom-particulierement-long"));
        // Sentinelle retirée après coup (même technique qu'InstanceDetail_LaunchErrorBanner_HoldsItsBoxes) :
        // simule une installation interrompue sans passer par un vrai téléchargement.
        fileSystem.RemoveFile(fileSystem.Path.Combine(
            provider.GetRequiredService<AppPaths>().VersionsDirectory,
            "1.20.4",
            FileSystemInstalledGameVersionRepository.CompletionMarkerFileName));

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        await detail.CheckInstanceCommand.ExecuteAsync(null);
        window.Settle();
        var dialog = shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();
        dialog.IsAllClear.ShouldBeFalse();

        window.ShouldHoldLayoutInvariantsAtEverySize("Dialogue du docteur d'instance, constats");

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
    public async Task ModBrowser_Grid_AddsAColumnAsTheWindowGrows()
    {
        // La grille fluide fait deux promesses : elle ne descend jamais sous deux colonnes au
        // plancher de la fenêtre, et elle en ajoute une quand la place le permet. Les deux se
        // vérifient ici, sur la même fenêtre que la garde d'invariants, parce qu'une grille qui
        // tient ses boîtes tout en rendant une seule colonne à 1280 tiendrait la lettre du contrat
        // sans en tenir l'esprit.
        using var provider = ResponsiveScenario.CreateProvider(out _, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        var expected = new (double Width, double Height, int Columns)[]
        {
            (ResponsiveWindowSizes.Floor.Width, ResponsiveWindowSizes.Floor.Height, 2),
            (1100, 700, 2),
            (1280, 800, 3),
            (1600, 900, 4),
        };

        foreach (var (width, height, columns) in expected)
        {
            window.Width = width;
            window.Height = height;
            window.Settle();

            var grid = window.GetVisualDescendants().OfType<FluidGridPanel>().ShouldHaveSingleItem();
            grid.ColumnCount.ShouldBe(columns, $"à {width}x{height}, la grille devrait rendre {columns} colonnes");

            // Et les cartes remplissent réellement la largeur : une colonne qui garderait sa
            // largeur minimale laisserait la bande vide que cette refonte supprime.
            var card = grid.Children[0];
            card.Bounds.Width.ShouldBeGreaterThanOrEqualTo(grid.MinItemWidth - 1);
            var used = (card.Bounds.Width * columns) + (grid.ColumnSpacing * (columns - 1));
            // Un point de tolérance par colonne : l'alignement au pixel arrondit chaque largeur de
            // carte, et ces arrondis s'additionnent sur la rangée.
            used.ShouldBe(grid.Bounds.Width, tolerance: columns);
        }

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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModDetailDialog_WithARealModDbDescription_HoldsItsBoxes(bool light)
    {
        // La fiche du faux serveur tient en deux paragraphes : elle ne tend aucune boîte. Celle-ci
        // porte la description RÉELLE de Carry On, 29 Ko d'éditeur WYSIWYG — titres, listes à trois
        // niveaux, blocs de code, images hors CDN et 34 liens. C'est ce document-là qui décide si
        // le rendu tient à 960 points de large.
        using var provider = ResponsiveScenario.CreateProvider(out _, out var catalogHandler);
        catalogHandler.ModDb.CatalogJson = FakeModDbHandler.CatalogWith(FakeModDbHandler.CarryOnCatalogEntry);
        var window = ResponsiveScenario.ShowWindow(provider, light ? ThemeVariant.Light : ThemeVariant.Dark);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        var card = shell.ModBrowser.Results.Single(entry => entry.Name == "Carry On");
        await card.OpenCommand.ExecuteAsync(null);
        window.Settle();

        var dialog = shell.Overlay.Active.ShouldBeOfType<ModDetailDialogViewModel>();
        dialog.Description.Document.Blocks.Count.ShouldBeGreaterThan(100);
        dialog.HasLinks.ShouldBeTrue();
        await Task.WhenAll(dialog.Description.Images.Select(image => image.LoadCompletion));
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize(light ? "Fiche ModDB réelle, thème clair" : "Fiche ModDB réelle");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModInstallPlanDialog_WithAReleasePicker_HoldsItsBoxes(bool light)
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out var catalogHandler);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        catalogHandler.ModDb.CatalogJson = FakeModDbHandler.CatalogWith(FakeModDbHandler.CarryOnCatalogEntry);
        // Le navigateur filtre sur l'index de compatibilité du serveur dès qu'une instance est
        // ciblée : sans cette ligne, Carry On serait indexé mais jamais affiché.
        catalogHandler.ModDb.CompatibleModIds = [1783, 792, 890];
        // CarryOnLib n'est publiée que pour une autre série : la dépendance de la release dévoilée
        // tombe donc dans « publiée mais sans release compatible », donc dans l'offre décochée.
        catalogHandler.ModDb.CarryOnLibGameVersions = ["1.22.0"];
        var window = ResponsiveScenario.ShowWindow(provider, light ? ThemeVariant.Light : ThemeVariant.Dark);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.21.3");
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        await shell.ModBrowser.Results.Single(entry => entry.Name == "Carry On").InstallCommand.ExecuteAsync(null);
        window.Settle();

        var dialog = shell.Overlay.Active.ShouldBeOfType<ModInstallPlanDialogViewModel>();
        dialog.HasReleaseChoice.ShouldBeTrue();
        dialog.Releases.Count.ShouldBe(2);

        window.ShouldHoldLayoutInvariantsAtEverySize(
            light ? "Dialogue d'installation, sélecteur de version, thème clair" : "Dialogue d'installation, sélecteur de version");

        // Le dialogue le plus chargé qui existe : dévoilement ouvert, release non déclarée
        // compatible choisie (donc avertissement affiché), et la dépendance publiée sans release
        // compatible offerte à l'installation. C'est cet état-là qui tend le plus ses boîtes.
        dialog.ToggleAllReleasesCommand.Execute(null);
        dialog.SelectedRelease = dialog.Releases.Single(release => release.IsDeclaredIncompatible);
        await dialog.ReloadCompletion;
        window.Settle();
        dialog.ShowIncompatibleWarning.ShouldBeTrue();
        dialog.HasInstallableAnyway.ShouldBeTrue();

        window.ShouldHoldLayoutInvariantsAtEverySize(
            light
                ? "Dialogue d'installation, version incompatible dévoilée, thème clair"
                : "Dialogue d'installation, version incompatible dévoilée");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_AccountsTab_TwoFactorAndErrorStates_HoldTheirBoxes(bool refused)
    {
        // Les deux états les plus chargés de la section Comptes : la deuxième passe (champ de code
        // en plus du formulaire) et le message d'échec, qui est du texte long dans un cadre.
        using var provider = ResponsiveScenario.CreateProvider(out _, out var handler);
        handler.Auth.Responses = refused ? [FakeVsAuthHandler.InvalidCredentialsJson] : [FakeVsAuthHandler.TwoFactorJson];
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        shell.Settings.SelectTabCommand.Execute(SettingsTab.Accounts);
        shell.Settings.Accounts.Email = "joueuse@example.invalid";
        shell.Settings.Accounts.Password = "mot-de-passe-de-test";
        await shell.Settings.Accounts.SignInCommand.ExecuteAsync(null);
        window.Settle();

        window.ShouldHoldLayoutInvariantsAtEverySize(refused ? "Réglages, Comptes, refus" : "Réglages, Comptes, code 2FA");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Settings_AccountsTab_SignedIn_HoldsItsBoxesInTheLightTheme()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out var handler);
        handler.Auth.Responses = [FakeVsAuthHandler.SuccessJson];
        var window = ResponsiveScenario.ShowWindow(provider, ThemeVariant.Light);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        shell.Settings.SelectTabCommand.Execute(SettingsTab.Accounts);
        shell.Settings.Accounts.Email = "joueuse@example.invalid";
        shell.Settings.Accounts.Password = "mot-de-passe-de-test";
        await shell.Settings.Accounts.SignInCommand.ExecuteAsync(null);
        window.Settle();
        shell.Settings.Accounts.IsSignedIn.ShouldBeTrue();

        window.ShouldHoldLayoutInvariantsAtEverySize("Réglages, Comptes, connecté, thème clair");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        window.Close();
    }

    [AvaloniaFact]
    public async Task SignOutDialog_HoldsItsBoxes()
    {
        using var provider = ResponsiveScenario.CreateProvider(out _, out var handler);
        handler.Auth.Responses = [FakeVsAuthHandler.SuccessJson];
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        shell.Settings.SelectTabCommand.Execute(SettingsTab.Accounts);
        shell.Settings.Accounts.Email = "joueuse@example.invalid";
        shell.Settings.Accounts.Password = "mot-de-passe-de-test";
        await shell.Settings.Accounts.SignInCommand.ExecuteAsync(null);
        shell.Settings.Accounts.SignOutCommand.Execute(null);
        window.Settle();
        shell.Overlay.Active.ShouldBeOfType<SignOutDialogViewModel>();

        window.ShouldHoldLayoutInvariantsAtEverySize("Déconnexion du compte");

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
        // Le scénario le plus chargé de l'écran (quatre lignes : dossier, version, compte, VS
        // Launcher) : celui qui tend le plus la boîte de la carte à la hauteur plancher de la
        // fenêtre (960x600).
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/" + ResponsiveScenario.LongInstanceName, "1.20.4");
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.ShowFirstRunIfNeeded();
        window.Settle();
        var firstRun = shell.Overlay.Active.ShouldBeOfType<FirstRunScreenViewModel>();
        firstRun.Steps.Count.ShouldBe(4);

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