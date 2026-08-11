using System.Runtime.Versioning;

namespace Prospect.Core.Common;

/// <summary>
/// Implémentation d'<see cref="IUnixFilePermissions"/> adossée à la BCL. Adaptateur système sans
/// logique propre, à l'image de <see cref="SystemClock"/> ou <see cref="SystemProcessRunner"/>,
/// donc exclu de la mesure de couverture : l'appel qu'il enveloppe n'existe pas sur Windows,
/// aucun test de la matrice ne peut le couvrir partout.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class SystemUnixFilePermissions : IUnixFilePermissions
{
    /// <inheritdoc />
    public void SetMode(string path, UnixFileMode mode) => File.SetUnixFileMode(path, mode);
}