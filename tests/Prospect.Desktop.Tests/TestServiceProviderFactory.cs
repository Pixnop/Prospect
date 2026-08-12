using System.IO.Abstractions.TestingHelpers;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;

namespace Prospect.Desktop.Tests;

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

    public static ServiceProvider Create(out MockFileSystem fileSystem, out FakeCatalogHandler catalogHandler)
    {
        fileSystem = new MockFileSystem();
        catalogHandler = new FakeCatalogHandler();
        var services = new ServiceCollection();
        CompositionRoot.ConfigureServices(services, fileSystem, catalogHandler);

        return services.BuildServiceProvider();
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
}