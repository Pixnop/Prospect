namespace Prospect.Core.Common;

/// <summary>
/// Port vers les permissions POSIX. Il existe parce que l'extraction d'un <c>.tar.gz</c> ne
/// restaure pas les bits d'exécution : sans un <c>chmod</c> explicite après coup, le binaire natif
/// <c>Vintagestory</c> ne démarre pas (docs/research/vslauncher-et-distribution.md, section d).
/// Le passer par une interface permet de tester ce comportement sur n'importe quel OS, y compris
/// Windows où <see cref="File.SetUnixFileMode(string, UnixFileMode)"/> n'existe pas.
/// </summary>
public interface IUnixFilePermissions
{
    /// <summary>Applique un mode POSIX à un fichier ou un dossier.</summary>
    void SetMode(string path, UnixFileMode mode);
}