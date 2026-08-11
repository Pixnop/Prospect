using System.IO.Abstractions;

namespace Prospect.Core.Launching;

/// <summary>Windows : <c>Vintagestory.exe</c> à la racine de l'installation.</summary>
public sealed class WindowsGameLaunchStrategy : IGameLaunchStrategy
{
    private readonly IFileSystem _fileSystem;

    public WindowsGameLaunchStrategy(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public string ResolveExecutablePath(string installDirectory) => _fileSystem.Path.Combine(installDirectory, "Vintagestory.exe");
}