using System.IO.Abstractions;

using Prospect.Core.Instances;

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

    /// <inheritdoc />
    /// <remarks>Rien à ajouter : <c>mesa_glthread</c> est une option des pilotes Mesa, qui n'existent pas ici.</remarks>
    public IReadOnlyDictionary<string, string> BuildEnvironment(InstanceLaunchSettings launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        return launch.Env;
    }
}