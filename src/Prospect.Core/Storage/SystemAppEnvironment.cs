namespace Prospect.Core.Storage;

/// <summary>
/// Implémentation d'<see cref="IAppEnvironment"/> adossée au système réel. C'est celle-ci que
/// la composition root de l'application enregistre ; les tests utilisent un double de test. À
/// l'image de <see cref="Prospect.Core.Common.SystemClock"/> pour
/// <see cref="Prospect.Core.Common.IClock"/> : le seul endroit du Core qui a le droit d'appeler
/// <see cref="Environment"/> et <see cref="OperatingSystem"/> directement, précisément parce
/// que son unique rôle est de les exposer derrière le port.
/// </summary>
public sealed class SystemAppEnvironment : IAppEnvironment
{
    /// <inheritdoc />
    public AppOperatingSystem CurrentOperatingSystem => GetCurrentOperatingSystem();

    private static AppOperatingSystem GetCurrentOperatingSystem()
        => OperatingSystem.IsWindows() switch
        {
            true => AppOperatingSystem.Windows,
            _ => OperatingSystem.IsMacOS() switch
            {
                true => AppOperatingSystem.MacOs,
                _ => AppOperatingSystem.Linux,
            },
        };

    /// <inheritdoc />
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <inheritdoc />
    public string GetFolderPath(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);
}