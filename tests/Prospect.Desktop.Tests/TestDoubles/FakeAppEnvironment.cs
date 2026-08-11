using Prospect.Core.Storage;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Double de test d'<see cref="IAppEnvironment"/> avec un système d'exploitation contrôlable par
/// le test (contrairement à <see cref="SystemAppEnvironment"/>, dont la valeur dépend de la
/// machine qui exécute les tests). Copie locale de l'équivalent de Prospect.Core.Tests (assemblies
/// distinctes).
/// </summary>
internal sealed class FakeAppEnvironment : IAppEnvironment
{
    public AppOperatingSystem CurrentOperatingSystem { get; set; } = AppOperatingSystem.Linux;

    public string? GetEnvironmentVariable(string name) => null;

    public string GetFolderPath(Environment.SpecialFolder folder) => "/home/test";
}