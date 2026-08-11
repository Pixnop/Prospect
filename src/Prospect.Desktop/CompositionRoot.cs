using System.IO.Abstractions;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Shell;

namespace Prospect.Desktop;

/// <summary>
/// Composition root (docs/architecture.md) : enregistre les adaptateurs du Core (système de
/// fichiers, horloge, environnement, chemins, stockage JSON), le domaine Instances, les services
/// Desktop transverses (panneau modal, toasts) et les ViewModels. <see cref="App"/> l'appelle avec
/// le système de fichiers réel ; les tests headless l'appellent directement avec un
/// <c>MockFileSystem</c>, pour exercer le même graphe de dépendances qu'en production (voir
/// tests/Prospect.Desktop.Tests). Les vues elles-mêmes ne sont jamais résolues par ce conteneur :
/// elles se construisent via le <see cref="ViewLocator"/>, par convention de nom.
/// </summary>
public static class CompositionRoot
{
    public static void ConfigureServices(IServiceCollection services, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(fileSystem);

        // Effets de bord du Core.
        services.AddSingleton(fileSystem);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IAppEnvironment, SystemAppEnvironment>();
        services.AddSingleton<AppPaths>();
        services.AddSingleton<JsonFileStore>();

        // Domaine Instances. Aucune IInstanceMetadataMigration n'est enregistrée : le schéma v1
        // est le premier, la résolution de IEnumerable<IInstanceMetadataMigration> par le
        // conteneur donne alors naturellement une séquence vide.
        services.AddSingleton<InstanceMetadataMigrationPipeline>();
        services.AddSingleton<IInstanceRepository, FileSystemInstanceRepository>();
        services.AddSingleton<InstanceService>();

        // Services Desktop transverses.
        services.AddSingleton<IOverlayService, OverlayService>();
        services.AddSingleton<IToastService, ToastService>();

        // ViewModels des pages construites eagerly (ShellViewModel construit lui-même les pages
        // placeholder). Singleton : une seule instance de chaque pour la durée de vie de l'app.
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<ShellViewModel>();

        // Fenêtre.
        services.AddSingleton<MainWindow>();
    }
}