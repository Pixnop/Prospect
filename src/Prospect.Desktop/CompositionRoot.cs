using System.IO.Abstractions;

using Avalonia;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Auth;
using Prospect.Core.Backups;
using Prospect.Core.Common;
using Prospect.Core.Diagnostics;
using Prospect.Core.GameVersions;
using Prospect.Core.Http;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Launching;
using Prospect.Core.Migration;
using Prospect.Core.ModDb;
using Prospect.Core.Modpacks;
using Prospect.Core.Runtime;
using Prospect.Core.Settings;
using Prospect.Core.Settings.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Downloads;
using Prospect.Desktop.ViewModels.FirstRun;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Migration;
using Prospect.Desktop.ViewModels.Modpacks;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Settings;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Versions;
using Prospect.Desktop.ViewModels.Wizard;

namespace Prospect.Desktop;

/// <summary>
/// Composition root (docs/architecture.md) : enregistre les adaptateurs du Core (système de
/// fichiers, horloge, environnement, chemins, stockage JSON, processus, permissions POSIX), les
/// domaines Instances et GameVersions, les services Desktop transverses (panneau modal, toasts,
/// fil UI) et les ViewModels. <see cref="App"/> l'appelle avec le système de fichiers réel ; les
/// tests headless l'appellent directement avec un <c>MockFileSystem</c>, pour exercer le même
/// graphe de dépendances qu'en production (voir tests/Prospect.Desktop.Tests). Les vues elles-mêmes
/// ne sont jamais résolues par ce conteneur : elles se construisent via le
/// <see cref="ViewLocator"/>, par convention de nom.
/// </summary>
public static class CompositionRoot
{
    /// <summary>
    /// Enregistre tout le graphe applicatif.
    /// </summary>
    /// <param name="services">Collection à peupler.</param>
    /// <param name="fileSystem">Système de fichiers, réel dans l'application, factice dans les tests.</param>
    /// <param name="httpMessageHandler">
    /// Gestionnaire HTTP des deux clients. <see langword="null"/> en production (pile réseau par
    /// défaut) ; les tests y passent un gestionnaire factice, ce qui rend structurellement
    /// impossible le moindre appel réseau réel depuis la suite ou depuis la CI.
    /// </param>
    public static void ConfigureServices(IServiceCollection services, IFileSystem fileSystem, HttpMessageHandler? httpMessageHandler = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(fileSystem);

        // Effets de bord du Core.
        services.AddSingleton(fileSystem);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IAppEnvironment, SystemAppEnvironment>();
        services.AddSingleton<IProcessRunner, SystemProcessRunner>();
        services.AddSingleton<IUnixFilePermissions, SystemUnixFilePermissions>();
        services.AddSingleton<IExternalUrlOpener, ExternalUrlOpener>();
        services.AddSingleton<AppPaths>();
        services.AddSingleton<JsonFileStore>();

        // Domaine Instances. InstanceMetadataV1ToV2Migration (chantier Sauvegardes) est la
        // première vraie migration enregistrée : IEnumerable<IInstanceMetadataMigration> ne
        // donnait qu'une séquence vide jusqu'ici, le schéma v1 étant le premier schéma réel.
        services.AddSingleton<IInstanceMetadataMigration, InstanceMetadataV1ToV2Migration>();
        services.AddSingleton<InstanceMetadataMigrationPipeline>();
        services.AddSingleton<IInstanceRepository, FileSystemInstanceRepository>();
        services.AddSingleton<InstanceService>();

        // Sauvegardes d'instance : n'a besoin de rien de plus qu'IInstanceRepository (topologie
        // disque) et les effets de bord déjà enregistrés plus haut. Enregistré ici, avant
        // AddLaunching, puisque GameLauncher en dépend (sauvegarde automatique de pré-lancement).
        services.AddSingleton<InstanceBackupService>();

        AddSettings(services);
        AddAuth(services, httpMessageHandler);
        AddGameVersions(services, httpMessageHandler);
        AddLaunching(services);
        AddModDb(services, httpMessageHandler);
        AddModpacks(services);
        AddMigration(services);

        // Docteur d'instance (diagnostic local hors ligne) : composition pure de ce qu'AddGameVersions/
        // AddLaunching/AddModDb ont déjà enregistré plus haut, aucun adaptateur propre à ce domaine —
        // et surtout, aucun client HTTP dans son graphe de dépendances.
        services.AddSingleton<InstanceDoctor>();

        // Services Desktop transverses.
        services.AddSingleton<IOverlayService, OverlayService>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IModUpdateCheckCache, ModUpdateCheckCache>();

        // L'Application Avalonia elle-même : déjà construite et courante avant que ce conteneur ne
        // soit peuplé (App.Initialize / TestAppBuilder pour les tests headless), résolue telle
        // quelle plutôt que redemandée à un Current statique depuis ThemeService.
        services.AddSingleton(_ => Application.Current!);
        services.AddSingleton<ThemeService>();

        // Cache des logos du navigateur de mods : un délai plus court que celui du catalogue, une
        // vignette décorative ne justifie pas d'attendre aussi longtemps qu'un appel API (voir
        // IModLogoCache).
        services.AddSingleton<IModLogoCache>(provider => new ModLogoCache(
            CreateHttpClient(httpMessageHandler, TimeSpan.FromSeconds(15))));

        // Sélecteur de fichiers du système (export/import de modpack). Fabrique vers MainWindow
        // plutôt que la fenêtre elle-même : voir la remarque d'AvaloniaFilePickerService sur le
        // cycle que la résolution directe créerait avec ShellViewModel/HomeViewModel.
        services.AddSingleton<Func<MainWindow>>(provider => provider.GetRequiredService<MainWindow>);
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();

        // L'import de modpack est éphémère (un par fichier choisi) et paramétré par le chemin
        // source, connu seulement à l'usage : même fabrique paramétrée que la page de détail
        // d'instance ci-dessous, pour la même raison.
        services.AddSingleton<Func<string, ImportModpackViewModel>>(provider => sourcePath => new ImportModpackViewModel(
            sourcePath,
            provider.GetRequiredService<ModpackImportService>(),
            provider.GetRequiredService<IOverlayService>(),
            provider.GetRequiredService<IToastService>(),
            provider.GetRequiredService<IUiDispatcher>()));

        // Le dialogue d'adoption VS Launcher est éphémère (un par ouverture) et paramétré par le
        // résultat de détection, connu seulement à l'usage (Réglages ou rappel de premier
        // lancement) : même fabrique paramétrée que l'import de modpack ci-dessus.
        services.AddSingleton<Func<VslDetectionResult, AdoptVslViewModel>>(provider => detection => new AdoptVslViewModel(
            detection,
            provider.GetRequiredService<VslAdoptionService>(),
            provider.GetRequiredService<IInstalledGameVersionRepository>(),
            provider.GetRequiredService<IFileSystem>(),
            provider.GetRequiredService<IOverlayService>(),
            provider.GetRequiredService<IToastService>(),
            provider.GetRequiredService<IUiDispatcher>()));

        // ViewModels des pages construites eagerly. Singleton : une seule instance de chaque pour
        // la durée de vie de l'app. FirstRunViewModel et SettingsViewModel dépendent tous deux de
        // HomeViewModel (rafraîchir la grille après une adoption) : HomeViewModel doit donc déjà
        // être enregistré, même si l'ordre des appels AddSingleton n'a pas d'importance pour la
        // résolution elle-même (résolue paresseusement, pas à l'enregistrement).
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<FirstRunViewModel>();
        services.AddSingleton<FirstRunScreenViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<VersionsViewModel>();
        services.AddSingleton<ModBrowserViewModel>();
        services.AddSingleton<DownloadsViewModel>();
        services.AddSingleton<ShellViewModel>();

        // Le wizard est éphémère (un par création d'instance) et l'Accueil l'obtient par fabrique
        // plutôt qu'en portant lui-même toutes ses dépendances.
        services.AddTransient<WizardViewModel>();
        services.AddSingleton<Func<WizardViewModel>>(provider => provider.GetRequiredService<WizardViewModel>);

        // La page de détail d'instance est elle aussi éphémère (une par instance ouverte), mais
        // paramétrée par un slug que le conteneur ne connaît qu'à l'usage : la fabrique construit
        // donc l'instance à la main plutôt que de déclarer un AddTransient résolu par type, seule
        // façon d'injecter un paramètre runtime avec Microsoft.Extensions.DependencyInjection.
        services.AddSingleton<Func<string, InstanceDetailViewModel>>(provider => slug => new InstanceDetailViewModel(
            slug,
            provider.GetRequiredService<InstanceService>(),
            provider.GetRequiredService<IInstanceRepository>(),
            provider.GetRequiredService<GameLauncher>(),
            provider.GetRequiredService<RunningInstanceTracker>(),
            provider.GetRequiredService<InstanceBackupService>(),
            provider.GetRequiredService<IInstalledModRepository>(),
            provider.GetRequiredService<ModInstallService>(),
            provider.GetRequiredService<ModUpdateChecker>(),
            provider.GetRequiredService<IModUpdateCheckCache>(),
            provider.GetRequiredService<InstanceDoctor>(),
            provider.GetRequiredService<IAppEnvironment>(),
            provider.GetRequiredService<IFileSystem>(),
            provider.GetRequiredService<IOverlayService>(),
            provider.GetRequiredService<IToastService>(),
            provider.GetRequiredService<IUiDispatcher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ModpackExportService>(),
            provider.GetRequiredService<IFilePickerService>()));

        // Fenêtre.
        services.AddSingleton<MainWindow>();
    }

    // Réglages globaux (prospect.json, docs/architecture.md). Comme pour Instances, aucune
    // ISettingsMigration n'est enregistrée : le schéma v1 est le premier. SettingsService expose
    // des valeurs par défaut saines dès sa construction (voir sa docstring) ; c'est
    // App.OnFrameworkInitializationCompleted qui appelle LoadAsync explicitement, tôt, avant de
    // construire la première fenêtre.
    private static void AddSettings(IServiceCollection services)
    {
        services.AddSingleton<SettingsMigrationPipeline>();
        services.AddSingleton<SettingsService>();
    }

    // Compte Vintage Story (docs/architecture.md, « Après le MVP ») : un client HTTP à part, avec un
    // délai court — une connexion qui traîne au-delà de quinze secondes n'aboutira pas — et le
    // stockage de session derrière ISecretStore, le seul endroit du graphe qui sait où vit le secret.
    private static void AddAuth(IServiceCollection services, HttpMessageHandler? httpMessageHandler)
    {
        services.AddSingleton(_ => new VsAccountClient(CreateHttpClient(httpMessageHandler, TimeSpan.FromSeconds(15))));
        services.AddSingleton<ISecretStore, FileSecretStore>();
        services.AddSingleton<VsAccountService>();
        services.AddSingleton<ClientSettingsSessionWriter>();
    }

    // Les deux clients HTTP sont construits ici, et pas résolus par type, parce qu'ils n'ont pas le
    // même contrat de temps. Celui du catalogue a un délai de requête normal ; celui des
    // téléchargements n'en a aucun, sans quoi un client de 600 Mo serait coupé en plein transfert
    // (voir la remarque de DownloadManager). Le seul garde-fou côté téléchargement est le délai
    // d'inactivité par lecture.
    private static void AddGameVersions(IServiceCollection services, HttpMessageHandler? httpMessageHandler)
    {
        services.AddSingleton<IGameVersionCatalog>(provider => new HttpGameVersionCatalog(
            CreateHttpClient(httpMessageHandler, TimeSpan.FromSeconds(30)),
            provider.GetRequiredService<JsonFileStore>(),
            provider.GetRequiredService<AppPaths>(),
            provider.GetRequiredService<IClock>()));

        // DownloadManager est un singleton construit une seule fois, paresseusement, à la première
        // résolution : le parallélisme choisi par l'utilisateur (Réglages, section Réseau) doit
        // donc déjà être chargé à cet instant. C'est le cas en production (LoadAsync tourne avant
        // la première fenêtre, voir App) comme dans la quasi-totalité des tests (aucun ne touche
        // SettingsService, donc DownloadOptions.Default.MaxParallelDownloads == la valeur par
        // défaut de ProspectSettings, comportement inchangé). Un changement du réglage APRÈS que ce
        // manager existe déjà s'applique à la prochaine ouverture de l'app : le sémaphore interne
        // de DownloadManager est dimensionné une fois à la construction, le redimensionner à chaud
        // est un chantier à part que rien ne demande encore.
        services.AddSingleton<IDownloadManager>(provider =>
        {
            var maxParallelDownloads = provider.GetRequiredService<SettingsService>().Current.Downloads.MaxParallelDownloads;
            var options = DownloadOptions.Default with { MaxParallelDownloads = maxParallelDownloads };

            return new DownloadManager(
                CreateHttpClient(httpMessageHandler, Timeout.InfiniteTimeSpan),
                provider.GetRequiredService<IFileSystem>(),
                provider.GetRequiredService<AppPaths>(),
                provider.GetRequiredService<IClock>(),
                options: options);
        });

        services.AddSingleton<IInstalledGameVersionRepository, FileSystemInstalledGameVersionRepository>();

        // Stratégie d'installation : le seul endroit du projet où l'OS courant décide de quelque
        // chose (docs/architecture.md, « Strategy par OS »). Une fois résolue ici, plus rien en
        // aval ne sait sur quel système il tourne.
        services.AddSingleton<LinuxGameInstallStrategy>();
        services.AddSingleton<WindowsGameInstallStrategy>();
        services.AddSingleton<MacOsGameInstallStrategy>();
        services.AddSingleton<GameInstallStrategySelector>();
        services.AddSingleton<IGameInstallStrategy>(provider => provider
            .GetRequiredService<GameInstallStrategySelector>()
            .Resolve(provider.GetRequiredService<IAppEnvironment>().CurrentOperatingSystem));

        services.AddSingleton<GameInstallService>();
    }

    // Le client ModDB partage la file de téléchargements du domaine GameVersions plutôt que d'avoir
    // la sienne : c'est le contrat du DownloadManager transverse (docs/architecture.md), et le
    // popover Téléchargements montre donc mods et versions du jeu dans la même liste. Son propre
    // HttpClient, en revanche, est celui des appels courts, avec un délai de requête normal.
    private static void AddModDb(IServiceCollection services, HttpMessageHandler? httpMessageHandler)
    {
        services.AddSingleton<IModDbClient>(provider => new ModDbClient(
            CreateHttpClient(httpMessageHandler, TimeSpan.FromSeconds(30)),
            provider.GetRequiredService<JsonFileStore>(),
            provider.GetRequiredService<AppPaths>(),
            provider.GetRequiredService<IClock>()));

        services.AddSingleton<ModArchiveReader>();
        services.AddSingleton<IModStateConvention, DisabledSuffixModStateConvention>();
        services.AddSingleton<IInstalledModRepository, FileSystemInstalledModRepository>();
        services.AddSingleton<ModInstallService>();
        services.AddSingleton<ModUpdateChecker>();
    }

    // Feature 5 (docs/architecture.md, « 5. Modpacks ») : aucun adaptateur propre à ce domaine,
    // seulement une composition de ce qu'AddGameVersions/AddModDb/AddInstances ont déjà enregistré
    // plus haut, résolue automatiquement par le conteneur sur la forme constructeur.
    private static void AddModpacks(IServiceCollection services)
    {
        services.AddSingleton<ModpackExportService>();
        services.AddSingleton<ModpackImportService>();
    }

    // Adoption des installations VS Launcher (docs/research/vslauncher-et-distribution.md, section
    // f) : comme AddModpacks, aucun adaptateur propre à ce domaine, seulement une composition de ce
    // qu'AddGameVersions/AddInstances ont déjà enregistré plus haut.
    private static void AddMigration(IServiceCollection services)
    {
        services.AddSingleton<VslDetector>();
        services.AddSingleton<VslAdoptionService>();
    }

    // Miroir d'AddGameVersions pour le lancement (docs/architecture.md, section « 3. Lancement ») :
    // détection du runtime .NET, stratégie de lancement par OS résolue une fois ici, suivi des
    // processus en cours, façade GameLauncher. IProcessRunner et IClock sont déjà enregistrés plus
    // haut (effets de bord du Core), réutilisés tels quels.
    private static void AddLaunching(IServiceCollection services)
    {
        services.AddSingleton<IDotnetLocator, DotnetLocator>();

        services.AddSingleton<LinuxGameLaunchStrategy>();
        services.AddSingleton<WindowsGameLaunchStrategy>();
        services.AddSingleton<MacGameLaunchStrategy>();
        services.AddSingleton<GameLaunchStrategySelector>();
        services.AddSingleton<IGameLaunchStrategy>(provider => provider
            .GetRequiredService<GameLaunchStrategySelector>()
            .Resolve(provider.GetRequiredService<IAppEnvironment>().CurrentOperatingSystem));

        services.AddSingleton<RunningInstanceTracker>();
        services.AddSingleton<GameLauncher>();
    }

    // disposeHandler: false parce qu'un même gestionnaire factice sert les deux clients d'un test.
    private static HttpClient CreateHttpClient(HttpMessageHandler? handler, TimeSpan timeout)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = timeout;

        return client;
    }
}