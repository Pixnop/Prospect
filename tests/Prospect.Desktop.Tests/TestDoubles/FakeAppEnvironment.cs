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

    /// <summary>
    /// Racine rendue pour tout dossier spécial. Réglable pour les tests qui feignent l'OS dans le
    /// conteneur RÉEL : là, les chemins doivent rester ceux de la machine, seul l'OS est feint.
    /// </summary>
    public string HomeDirectory { get; set; } = "/home/test";

    public string? GetEnvironmentVariable(string name) => null;

    public string GetFolderPath(Environment.SpecialFolder folder) => HomeDirectory;
}