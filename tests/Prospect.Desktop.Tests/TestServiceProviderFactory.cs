using System.IO.Abstractions.TestingHelpers;

using Microsoft.Extensions.DependencyInjection;

namespace Prospect.Desktop.Tests;

/// <summary>
/// Construit le même graphe de dépendances que la composition root réelle
/// (<see cref="CompositionRoot"/>), avec un <see cref="MockFileSystem"/> à la place du système de
/// fichiers réel : aucun test de cet assembly ne touche le disque, tout en exerçant le DI réel
/// (docs/architecture.md, exigence de test « le shell s'instancie avec le DI réel sur MockFileSystem »).
/// </summary>
internal static class TestServiceProviderFactory
{
    public static ServiceProvider Create(out MockFileSystem fileSystem)
    {
        fileSystem = new MockFileSystem();
        var services = new ServiceCollection();
        CompositionRoot.ConfigureServices(services, fileSystem);
        return services.BuildServiceProvider();
    }
}