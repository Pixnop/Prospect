namespace Prospect.Core.Launching;

/// <summary>
/// Exécutable du jeu à lancer pour un OS donné. Pattern Strategy par OS (docs/architecture.md,
/// « Strategy par OS »), miroir de <see cref="Prospect.Core.GameVersions.IGameInstallStrategy"/>
/// pour le lancement plutôt que l'installation : une interface, une implémentation par
/// plateforme, sélection à la composition, jamais de <c>if (OperatingSystem.IsWindows())</c>
/// disséminé dans <c>GameLauncher</c>.
/// </summary>
public interface IGameLaunchStrategy
{
    /// <summary>Chemin complet de l'exécutable du jeu dans <paramref name="installDirectory"/>.</summary>
    /// <exception cref="MacLaunchNotSupportedException">
    /// macOS : le lancement n'est pas pris en charge par cette version de Prospect.
    /// </exception>
    string ResolveExecutablePath(string installDirectory);
}