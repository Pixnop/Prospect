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
    public Task InstallAsync(string archivePath, string targetDirectory, CancellationToken cancellationToken = default)
        => _installer.InstallAsync(archivePath, targetDirectory, cancellationToken);
}