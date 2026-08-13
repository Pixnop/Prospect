using Prospect.Core.Instances;

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

    /// <summary>
    /// Environnement du processus de jeu : les variables de l'instance, plus celles que cet OS pose
    /// lui-même.
    /// </summary>
    /// <remarks>
    /// Deuxième membre de la stratégie parce que la seule option d'environnement que Prospect
    /// connaisse par son nom, <c>mesa_glthread</c>, est propre à Linux et n'a aucun sens ailleurs.
    /// La poser depuis <c>GameLauncher</c> demanderait un test d'OS dans un service qui n'a
    /// justement pas à savoir sur lequel il tourne. Membre EXIGÉ plutôt qu'implémentation par
    /// défaut : rendre l'environnement de l'instance tel quel est le comportement de Windows et de
    /// macOS, mais c'est une décision, et une décision se lit dans la classe qui la prend.
    /// </remarks>
    IReadOnlyDictionary<string, string> BuildEnvironment(InstanceLaunchSettings launch);
}