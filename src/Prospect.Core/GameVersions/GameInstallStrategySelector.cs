using Prospect.Core.Storage;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Table de correspondance entre système d'exploitation et stratégie d'installation. Elle existe
/// pour qu'il n'y ait qu'un seul endroit dans tout le projet où l'OS courant décide de quoi que ce
/// soit en matière d'installation : la composition root résout la stratégie une fois au démarrage,
/// et plus rien en aval ne sait sur quel OS il tourne.
/// </summary>
public sealed class GameInstallStrategySelector
{
    private readonly Dictionary<AppOperatingSystem, IGameInstallStrategy> _strategies;

    public GameInstallStrategySelector(
        LinuxGameInstallStrategy linux,
        WindowsGameInstallStrategy windows,
        MacOsGameInstallStrategy macOs)
    {
        ArgumentNullException.ThrowIfNull(linux);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(macOs);

        _strategies = new Dictionary<AppOperatingSystem, IGameInstallStrategy>
        {
            [AppOperatingSystem.Linux] = linux,
            [AppOperatingSystem.Windows] = windows,
            [AppOperatingSystem.MacOs] = macOs,
        };
    }

    /// <summary>Stratégie à utiliser sur cet OS.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Valeur d'OS inconnue de la table.</exception>
    public IGameInstallStrategy Resolve(AppOperatingSystem operatingSystem)
        => _strategies.TryGetValue(operatingSystem, out var strategy)
            ? strategy
            : throw new ArgumentOutOfRangeException(nameof(operatingSystem), operatingSystem, "Aucune stratégie d'installation pour ce système.");
}