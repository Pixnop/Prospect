using System.IO.Abstractions;

using Prospect.Core.Common;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Installation macOS : même mécanique que Linux (extraction du <c>.tar.gz</c> puis restauration
/// des bits d'exécution), mais avec deux plateformes possibles, <c>mac-arm64</c> en priorité et
/// <c>mac-x64</c> en repli, comme le fait le catalogue officiel.
/// </summary>
/// <remarks>
/// macOS est traité comme une cible réelle dès maintenant, téléchargement et installation
/// compris, même si le bouton « Jouer » attendra une vraie machine de test. VS Launcher a laissé
/// ses utilisateurs mac des années avec un « not yet supported » faute d'avoir anticipé
/// (docs/research/vslauncher-et-distribution.md, implication 12).
/// </remarks>
public sealed class MacOsGameInstallStrategy : IGameInstallStrategy
{
    private readonly TarGzGameInstaller _installer;

    public MacOsGameInstallStrategy(IFileSystem fileSystem, IUnixFilePermissions permissions)
    {
        _installer = new TarGzGameInstaller(fileSystem, permissions);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> PlatformKeys { get; } = [GamePlatforms.MacArm64, GamePlatforms.MacX64];

    /// <inheritdoc />
    /// <remarks>
    /// L'archive mac livre un bundle : le binaire vit sous <c>Vintagestory.app/Contents/MacOS/</c>.
    /// Le binaire nu à la racine est gardé en repli parce que rien ne garantit la forme du bundle
    /// sur toutes les versions publiées, et qu'un lancement mac reste à écrire de toute façon.
    /// </remarks>
    public IReadOnlyList<GameExecutableLocation> ExpectedExecutables { get; } =
    [
        GameExecutableLocation.Of("Vintagestory.app", "Contents", "MacOS", "Vintagestory"),
        GameExecutableLocation.Of("Vintagestory"),
    ];

    /// <inheritdoc />
    public Task InstallAsync(
        string archivePath,
        string targetDirectory,
        IProgress<GameInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => _installer.InstallAsync(archivePath, targetDirectory, progress, cancellationToken);
}