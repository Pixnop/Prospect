using System.IO.Abstractions;

using Prospect.Core.Common;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Installation Linux : extraction du <c>.tar.gz</c> client puis <c>chmod 755</c> récursif.
/// </summary>
public sealed class LinuxGameInstallStrategy : IGameInstallStrategy
{
    private readonly TarGzGameInstaller _installer;

    public LinuxGameInstallStrategy(IFileSystem fileSystem, IUnixFilePermissions permissions)
    {
        _installer = new TarGzGameInstaller(fileSystem, permissions);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> PlatformKeys { get; } = [GamePlatforms.Linux];

    /// <inheritdoc />
    /// <remarks>
    /// Le binaire natif d'abord, exactement comme <c>Launching.LinuxGameLaunchStrategy</c> le
    /// cherche, puis l'ancien build .NET Framework en <c>.exe</c> que les archives d'avant la
    /// bascule native contiennent encore (docs/research/vslauncher-et-distribution.md, point d).
    /// </remarks>
    public IReadOnlyList<GameExecutableLocation> ExpectedExecutables { get; } =
    [
        GameExecutableLocation.Of("Vintagestory"),
        GameExecutableLocation.Of("Vintagestory.exe"),
    ];

    /// <inheritdoc />
    public Task InstallAsync(
        string archivePath,
        string targetDirectory,
        IProgress<GameInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => _installer.InstallAsync(archivePath, targetDirectory, progress, cancellationToken);
}