using Prospect.Core.Storage;

namespace Prospect.Core.Tests.Storage;

/// <summary>
/// Double de test d'<see cref="IAppEnvironment"/> : OS courant, variables d'environnement et
/// dossiers spéciaux entièrement contrôlés par le test, pour exercer les trois branches de
/// calcul de racine d'<see cref="AppPaths"/> indépendamment de l'OS qui exécute réellement la
/// suite (XDG posé ou absent, APPDATA posé ou absent...).
/// </summary>
internal sealed class FakeAppEnvironment : IAppEnvironment
{
    private readonly Dictionary<string, string?> _environmentVariables = new(StringComparer.Ordinal);
    private readonly Dictionary<Environment.SpecialFolder, string> _folderPaths = new();

    public AppOperatingSystem CurrentOperatingSystem { get; set; } = AppOperatingSystem.Linux;

    public void SetEnvironmentVariable(string name, string? value) => _environmentVariables[name] = value;

    public void SetFolderPath(Environment.SpecialFolder folder, string path) => _folderPaths[folder] = path;

    public string? GetEnvironmentVariable(string name)
        => _environmentVariables.TryGetValue(name, out var value) ? value : null;

    public string GetFolderPath(Environment.SpecialFolder folder)
        => _folderPaths.TryGetValue(folder, out var path) ? path : string.Empty;
}