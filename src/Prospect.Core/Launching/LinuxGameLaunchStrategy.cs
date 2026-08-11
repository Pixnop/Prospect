using System.IO.Abstractions;

namespace Prospect.Core.Launching;

/// <summary>Linux : le binaire natif <c>Vintagestory</c>, sans extension, à la racine de l'installation.</summary>
public sealed class LinuxGameLaunchStrategy : IGameLaunchStrategy
{
    private readonly IFileSystem _fileSystem;

    public LinuxGameLaunchStrategy(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public string ResolveExecutablePath(string installDirectory) => _fileSystem.Path.Combine(installDirectory, "Vintagestory");
}