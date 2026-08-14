using System.IO.Abstractions.TestingHelpers;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Migration;
using Prospect.Core.Runtime;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;

namespace Prospect.Desktop.Tests;

/// <summary>
/// Les trois derniers ports que le conteneur de test substitue, rendus à l'appelant pour qu'un
/// parcours puisse les piloter : démarrer et faire sortir un processus de jeu, décider du verdict
/// du runtime, répondre à un sélecteur de fichiers. Ce sont les seuls effets de bord qu'un parcours
/// de bout en bout doit encore tenir à la main une fois le disque et le réseau déjà feints.
/// </summary>
internal sealed record JourneySeams(
    FakeProcessRunner ProcessRunner,
    FakeDotnetLocator DotnetLocator,
    FakeFilePickerService FilePicker);

/// <summary>
/// Construit le même graphe de dépendances que la composition root réelle
/// (<see cref="CompositionRoot"/>), avec un <see cref="MockFileSystem"/> à la place du système de
/// fichiers réel et un gestionnaire HTTP factice à la place de la pile réseau : aucun test de cet
/// assembly ne touche le disque ni le réseau, tout en exerçant le DI réel (docs/architecture.md,
/// exigence de test « le shell s'instancie avec le DI réel sur MockFileSystem »).
/// </summary>
internal static class TestServiceProviderFactory
{
    public static ServiceProvider Create(out MockFileSystem fileSystem)
        => Create(out fileSystem, out _);

    /// <param name="operatingSystem">
    /// Système annoncé par <see cref="IAppEnvironment"/>. Substitué comme le sont le disque et le
    /// réseau, et pour la même raison : ce qui dépend de l'OS (la notice de la boîte de l'installeur
    /// Windows, par exemple) doit se tester depuis n'importe quelle machine. Laissé à
    /// <see langword="null"/>, c'est le vrai système qui répond, ce que veulent les tests dont le
    /// sujet n'est pas la plateforme.
    /// </param>
    public static ServiceProvider Create(
        out MockFileSystem fileSystem,
        out FakeCatalogHandler catalogHandler,
        AppOperatingSystem? operatingSystem = null)
    {
        fileSystem = new MockFileSystem();
        catalogHandler = new FakeCatalogHandler();
        var services = new ServiceCollection();
        CompositionRoot.ConfigureServices(services, fileSystem, catalogHandler);

        if (operatingSystem is { } forced)
        {
            var real = new SystemAppEnvironment();
            services.AddSingleton<IAppEnvironment>(new FakeAppEnvironment
            {
                CurrentOperatingSystem = forced,
                // Les chemins restent ceux de la vraie machine : seul l'OS annoncé est feint, et
                // AppPaths a déjà été composé sur l'environnement réel.
                HomeDirectory = real.GetFolderPath(Environment.SpecialFolder.UserProfile),
            });
        }

        // Troisième substitution, après le système de fichiers et le réseau, et pour exactement la
        // même raison : SystemUnixFilePermissions appelle la BCL directement, donc le VRAI disque,
        // pendant que tout le reste du conteneur travaille sur le MockFileSystem. La dernière
        // inscription gagne à la résolution, ce qui évite un troisième paramètre de seam sur la
        // composition root de production.
        services.AddSingleton<IUnixFilePermissions, RecordingUnixFilePermissions>();

        var provider = services.BuildServiceProvider();

        // Résolu ici pour la même raison qu'App.axaml.cs le résout au démarrage : il n'a pas de
        // client, seul son abonnement compte. Sans cette ligne, les tests headless tourneraient sur
        // une application qui, contrairement à la vraie, laisserait survivre l'état d'une instance
        // supprimée — exactement le genre d'écart entre double et production qui ne garde rien.
        provider.GetRequiredService<DeletedInstanceStateCleaner>();

        return provider;
    }

    /// <summary>
    /// Même graphe que <see cref="Create(out MockFileSystem, out FakeCatalogHandler, AppOperatingSystem?)"/>,
    /// plus les trois derniers ports du monde réel remplacés par des doubles pilotables : le
    /// lanceur de processus, la détection de runtime .NET et le sélecteur de fichiers.
    /// </summary>
    /// <remarks>
    /// C'est ce qui manquait pour qu'un PARCOURS complet tienne sur le conteneur réel. Les tests
    /// headless existants s'arrêtaient au bord du lancement : câbler un <c>GameLauncher</c> à la
    /// main à côté du conteneur (ce que font les tests de ViewModel) sort du graphe qu'on veut
    /// exercer, et laisser le vrai <c>SystemProcessRunner</c> ferait démarrer un processus sur la
    /// machine de test. Les substitutions se font APRÈS la composition root, la dernière
    /// inscription gagnant à la résolution : exactement le procédé déjà utilisé pour
    /// <see cref="IUnixFilePermissions"/> ci-dessus, et pour la même raison.
    /// </remarks>
    public static ServiceProvider CreateForJourney(
        out MockFileSystem fileSystem,
        out FakeCatalogHandler catalogHandler,
        out JourneySeams seams,
        AppOperatingSystem? operatingSystem = null)
    {
        fileSystem = new MockFileSystem();
        catalogHandler = new FakeCatalogHandler();
        var services = new ServiceCollection();
        CompositionRoot.ConfigureServices(services, fileSystem, catalogHandler);

        var real = new SystemAppEnvironment();
        services.AddSingleton<IAppEnvironment>(new FakeAppEnvironment
        {
            CurrentOperatingSystem = operatingSystem ?? AppOperatingSystem.Linux,
            HomeDirectory = real.GetFolderPath(Environment.SpecialFolder.UserProfile),
        });

        services.AddSingleton<IUnixFilePermissions, RecordingUnixFilePermissions>();

        var processRunner = new FakeProcessRunner();
        var dotnetLocator = new FakeDotnetLocator();
        var filePicker = new FakeFilePickerService();
        seams = new JourneySeams(processRunner, dotnetLocator, filePicker);

        services.AddSingleton<IProcessRunner>(processRunner);
        services.AddSingleton<IDotnetLocator>(dotnetLocator);
        services.AddSingleton<IFilePickerService>(filePicker);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<DeletedInstanceStateCleaner>();

        return provider;
    }

    /// <summary>
    /// Crée une instance de travail, pour les tests qui ont besoin que le navigateur de mods ait
    /// une cible d'installation (et donc des badges de compatibilité) plutôt que la seule entrée
    /// « Toutes les versions ».
    /// </summary>
    /// <returns>Le slug de l'instance créée.</returns>
    public static async Task<string> SeedTargetInstanceAsync(
        this ServiceProvider provider,
        string name = "Homestead",
        string gameVersion = "1.21.3")
    {
        ArgumentNullException.ThrowIfNull(provider);

        var service = provider.GetRequiredService<InstanceService>();
        var record = await service.CreateAsync(name, GameVersion.Parse(gameVersion));

        return record.Slug;
    }

    /// <summary>
    /// Pose sur le système de fichiers factice une installation de version complète, fichier
    /// sentinelle compris, pour les tests qui ont besoin d'une version déjà présente.
    /// </summary>
    public static void SeedInstalledVersion(this ServiceProvider provider, MockFileSystem fileSystem, string version)
    {
        var paths = provider.GetRequiredService<AppPaths>();
        var directory = fileSystem.Path.Combine(paths.VersionsDirectory, version);

        fileSystem.AddFile(fileSystem.Path.Combine(directory, "Vintagestory"), new MockFileData("binaire"));
        fileSystem.AddFile(
            fileSystem.Path.Combine(directory, FileSystemInstalledGameVersionRepository.CompletionMarkerFileName),
            new MockFileData(version));
    }

    /// <summary>
    /// Écrit un <c>config.json</c> de VS Launcher factice à l'emplacement par défaut de la VRAIE
    /// machine qui exécute les tests (<see cref="IAppEnvironment"/> n'est pas doublé dans ce
    /// conteneur, voir <see cref="CompositionRoot.ConfigureServices"/>) : même principe que
    /// <see cref="SeedInstalledVersion"/>, la racine réellement calculée est demandée au conteneur
    /// plutôt que supposée, pour que le test reste vrai sur n'importe quelle machine ou CI.
    /// </summary>
    public static string SeedVslInstallation(this ServiceProvider provider, MockFileSystem fileSystem, string installationPath, string version = "1.20.4")
    {
        ArgumentNullException.ThrowIfNull(provider);

        var environment = provider.GetRequiredService<IAppEnvironment>();
        var vslPaths = new VslPaths(environment);
        var configPath = vslPaths.ConfigFilePath;

        fileSystem.AddFile(configPath, new MockFileData($$"""
        {
          "installations": [ { "id": "a", "name": "Survie médiévale", "path": "{{installationPath.Replace("\\", "\\\\")}}", "version": "{{version}}" } ],
          "gameVersions": []
        }
        """));
        fileSystem.AddFile(fileSystem.Path.Combine(installationPath, "Mods", "carrycapacity.zip"), new MockFileData("mod-content"));

        return configPath;
    }
}