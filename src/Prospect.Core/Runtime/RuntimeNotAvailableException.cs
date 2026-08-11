namespace Prospect.Core.Runtime;

/// <summary>
/// Le runtime .NET requis par l'installation du jeu d'une instance n'est pas présent sur la
/// machine. Levée par <c>GameLauncher</c> avant de démarrer le moindre processus, avec un message
/// qui nomme précisément le runtime et la version à installer plutôt qu'un échec générique de
/// lancement (docs/architecture.md, « Si absent, le lancement échoue AVANT de spawner »).
/// </summary>
public sealed class RuntimeNotAvailableException : Exception
{
    public RuntimeNotAvailableException()
        : base("Le runtime .NET requis par le jeu n'est pas installé sur cette machine.")
    {
    }

    public RuntimeNotAvailableException(string message)
        : base(message)
    {
    }

    public RuntimeNotAvailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Construit l'exception pour un verdict de runtime manquant précis.</summary>
    public static RuntimeNotAvailableException For(GameRuntimeRequirement requirement)
        => new($"Le runtime .NET {requirement.FrameworkName} {requirement.Version} est requis par le jeu mais n'est pas installé. Installe-le avant de relancer cette instance.");
}