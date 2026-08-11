namespace Prospect.Core.GameVersions;

/// <summary>
/// Clés de plateforme du catalogue officiel, telles qu'elles apparaissent dans
/// <c>stable.json</c>/<c>unstable.json</c> (docs/research/vslauncher-et-distribution.md,
/// section b). Des constantes de chaîne plutôt qu'une énumération : le catalogue est un document
/// distant qui peut gagner une clé inconnue de nous sans prévenir, et une énumération fermée
/// transformerait cette nouveauté en échec de désérialisation.
/// </summary>
public static class GamePlatforms
{
    /// <summary>Installeur Inno Setup du client Windows. Il n'existe pas de build portable.</summary>
    public const string Windows = "windows";

    /// <summary>Installeur incrémental Windows, hors périmètre : Prospect installe toujours une version complète côte à côte.</summary>
    public const string WindowsUpdate = "windowsupdate";

    /// <summary>Archive <c>.tar.gz</c> du client Linux x64.</summary>
    public const string Linux = "linux";

    /// <summary>Archive du serveur dédié Linux, hors périmètre du launcher client.</summary>
    public const string LinuxServer = "linuxserver";

    /// <summary>Archive du serveur dédié Windows, hors périmètre du launcher client.</summary>
    public const string WindowsServer = "windowsserver";

    /// <summary>Archive <c>.tar.gz</c> du client macOS Intel.</summary>
    public const string MacX64 = "mac-x64";

    /// <summary>Archive <c>.tar.gz</c> du client macOS Apple Silicon.</summary>
    public const string MacArm64 = "mac-arm64";
}