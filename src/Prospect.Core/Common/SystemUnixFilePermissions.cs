namespace Prospect.Core.Common;

/// <summary>
/// Implémentation d'<see cref="IUnixFilePermissions"/> adossée à la BCL. Adaptateur système sans
/// logique propre, à l'image de <see cref="SystemClock"/> ou <see cref="SystemProcessRunner"/>,
/// donc exclu de la mesure de couverture : l'appel qu'il enveloppe n'existe pas sur Windows,
/// aucun test de la matrice ne peut le couvrir partout.
/// </summary>
public sealed class SystemUnixFilePermissions : IUnixFilePermissions
{
    /// <summary>
    /// Applique le mode, sauf sur Windows où la notion n'existe pas. Un no-op plutôt qu'une
    /// exception : cet adaptateur n'est de toute façon jamais sélectionné sur Windows (l'installeur
    /// Inno s'y charge des permissions), et le conteneur d'injection l'instancie sur les trois OS.
    /// </summary>
    public void SetMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(path, mode);
    }
}