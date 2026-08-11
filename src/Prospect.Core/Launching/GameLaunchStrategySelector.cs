using Prospect.Core.Storage;

namespace Prospect.Core.Launching;

/// <summary>
/// Table de correspondance entre système d'exploitation et stratégie de lancement. Miroir de
/// <see cref="Prospect.Core.GameVersions.GameInstallStrategySelector"/> pour le lancement : la
/// composition root résout la stratégie une fois au démarrage, et <c>GameLauncher</c> ne sait
/// jamais sur quel OS il tourne.
/// </summary>
public sealed class GameLaunchStrategySelector
{
    private readonly Dictionary<AppOperatingSystem, IGameLaunchStrategy> _strategies;

    public GameLaunchStrategySelector(LinuxGameLaunchStrategy linux, WindowsGameLaunchStrategy windows, MacGameLaunchStrategy macOs)
    {
        ArgumentNullException.ThrowIfNull(linux);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(macOs);

        _strategies = new Dictionary<AppOperatingSystem, IGameLaunchStrategy>
        {
            [AppOperatingSystem.Linux] = linux,
            [AppOperatingSystem.Windows] = windows,
            [AppOperatingSystem.MacOs] = macOs,
        };
    }

    /// <summary>Stratégie à utiliser sur cet OS.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Valeur d'OS inconnue de la table.</exception>
    public IGameLaunchStrategy Resolve(AppOperatingSystem operatingSystem)
        => _strategies.TryGetValue(operatingSystem, out var strategy)
            ? strategy
            : throw new ArgumentOutOfRangeException(nameof(operatingSystem), operatingSystem, "Aucune stratégie de lancement pour ce système.");
}