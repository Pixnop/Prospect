namespace Prospect.Core.GameVersions;

/// <summary>
/// Ce qu'il faut faire du fichier téléchargé pour obtenir une installation utilisable, sur un OS
/// donné. C'est le pattern Strategy exigé par docs/architecture.md : une interface, une
/// implémentation par plateforme, sélection à la composition, et surtout aucun
/// <c>if (OperatingSystem.IsWindows())</c> disséminé dans les services.
/// </summary>
public interface IGameInstallStrategy
{
    /// <summary>
    /// Clés de plateforme du catalogue acceptées, par ordre de préférence. macOS en déclare deux
    /// (<c>mac-arm64</c> puis <c>mac-x64</c>), les autres une seule.
    /// </summary>
    IReadOnlyList<string> PlatformKeys { get; }

    /// <summary>
    /// Installe le fichier téléchargé dans <paramref name="targetDirectory"/>. La méthode ne
    /// s'occupe ni du téléchargement, ni du fichier sentinelle : elle ne fait que produire le
    /// contenu.
    /// </summary>
    /// <exception cref="GameInstallFailedException">L'installation a échoué.</exception>
    Task InstallAsync(string archivePath, string targetDirectory, CancellationToken cancellationToken = default);
}