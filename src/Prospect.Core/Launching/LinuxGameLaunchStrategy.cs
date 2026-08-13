using System.IO.Abstractions;

using Prospect.Core.Instances;

namespace Prospect.Core.Launching;

/// <summary>Linux : le binaire natif <c>Vintagestory</c>, sans extension, à la racine de l'installation.</summary>
public sealed class LinuxGameLaunchStrategy : IGameLaunchStrategy
{
    /// <summary>
    /// Nom de la variable qui active le thread OpenGL de Mesa, en MINUSCULES.
    /// </summary>
    /// <remarks>
    /// <c>mesa_glthread</c> est une option de <c>driconf</c>, et Mesa lit son remplacement par
    /// variable d'environnement sous le nom exact de l'option. VS Launcher posait
    /// <c>MESA_GLTHREAD</c>, que rien ne lit : la parité recherchée ici est celle de la FONCTION
    /// offerte au joueur, pas celle de la faute de frappe.
    /// </remarks>
    public const string MesaGlThreadVariable = "mesa_glthread";

    private readonly IFileSystem _fileSystem;

    public LinuxGameLaunchStrategy(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public string ResolveExecutablePath(string installDirectory) => _fileSystem.Path.Combine(installDirectory, "Vintagestory");

    /// <inheritdoc />
    /// <remarks>
    /// La variable de l'instance l'emporte si l'utilisateur l'a écrite lui-même dans les variables
    /// libres : c'est sa machine, et la case à cocher n'est qu'un raccourci vers le même effet.
    /// </remarks>
    public IReadOnlyDictionary<string, string> BuildEnvironment(InstanceLaunchSettings launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        if (!launch.MesaGlThread)
        {
            return launch.Env;
        }

        var environment = new Dictionary<string, string>(launch.Env, StringComparer.Ordinal);
        if (!environment.ContainsKey(MesaGlThreadVariable))
        {
            environment[MesaGlThreadVariable] = "true";
        }

        return environment;
    }
}