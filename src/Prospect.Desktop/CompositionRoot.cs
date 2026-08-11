using System.IO.Abstractions;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Http;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Launching;
using Prospect.Core.ModDb;
using Prospect.Core.Modpacks;
using Prospect.Core.Runtime;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Downloads;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Modpacks;
using Prospect.Desktop.ViewModels.Mods;
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

        // Domaine Instances. Aucune IInstanceMetadataMigration n'est enregistrée : le schéma v1
        // est le premier, la résolution de IEnumerable<IInstanceMetadataMigration> par le
        // conteneur donne alors naturellement une séquence vide.
        services.AddSingleton<InstanceMetadataMigrationPipeline>();
        services.AddSingleton<IInstanceRepository, FileSystemInstanceRepository>();
        services.AddSingleton<InstanceService>();

        AddGameVersions(services, httpMessageHandler);
        AddLaunching(services);
        AddModDb(services, httpMessageHandler);
        AddModpacks(services);

        // Services Desktop transverses.
        services.AddSingleton<IOverlayService, OverlayService>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IModUpdateCheckCache, ModUpdateCheckCache>();

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

        // ViewModels des pages construites eagerly (ShellViewModel construit lui-même les pages
        // placeholder). Singleton : une seule instance de chaque pour la durée de vie de l'app.
        services.AddSingleton<HomeViewModel>();
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
            provider.GetRequiredService<IInstalledModRepository>(),
            provider.GetRequiredService<ModInstallService>(),
            provider.GetRequiredService<ModUpdateChecker>(),
            provider.GetRequiredService<IModUpdateCheckCache>(),
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

        services.AddSingleton<IDownloadManager>(provider => new DownloadManager(
            CreateHttpClient(httpMessageHandler, Timeout.InfiniteTimeSpan),
            provider.GetRequiredService<IFileSystem>(),
            provider.GetRequiredService<AppPaths>(),
            provider.GetRequiredService<IClock>()));

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