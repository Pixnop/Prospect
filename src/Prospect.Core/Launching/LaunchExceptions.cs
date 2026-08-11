using Prospect.Core.Common;

namespace Prospect.Core.Launching;

/// <summary>
/// Cette instance a déjà un processus de jeu en cours : on ne relance pas une session déjà
/// active (docs/architecture.md, « une instance en cours ne se relance pas »).
/// </summary>
public sealed class InstanceAlreadyRunningException : Exception
{
    public InstanceAlreadyRunningException()
        : base("Cette instance a déjà une session de jeu en cours.")
    {
        Slug = string.Empty;
    }

    public InstanceAlreadyRunningException(string slug)
        : base($"L'instance « {slug} » a déjà une session de jeu en cours.")
    {
        Slug = slug;
    }

    public InstanceAlreadyRunningException(string slug, Exception innerException)
        : base($"L'instance « {slug} » a déjà une session de jeu en cours.", innerException)
    {
        Slug = slug;
    }

    /// <summary>Slug de l'instance dont une session est déjà en cours.</summary>
    public string Slug { get; }
}

/// <summary>
/// Le lancement du jeu sur macOS n'est pas pris en charge par cette version de Prospect. Décision
/// d'architecture documentée (docs/architecture.md, section « 2. Versions du jeu ») : le
/// téléchargement et l'installation mac sont traités comme une cible réelle, mais le bouton Jouer
/// attend une vraie machine de test avant d'être activé.
/// </summary>
public sealed class MacLaunchNotSupportedException : Exception
{
    public MacLaunchNotSupportedException()
        : base("Le lancement du jeu sur macOS n'est pas encore pris en charge par Prospect.")
    {
    }

    public MacLaunchNotSupportedException(string message)
        : base(message)
    {
    }

    public MacLaunchNotSupportedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>La version du jeu que demande cette instance n'est pas installée, ou son installation est incomplète.</summary>
public sealed class GameVersionNotInstalledException : Exception
{
    public GameVersionNotInstalledException()
        : base("La version du jeu demandée par cette instance n'est pas installée.")
    {
    }

    public GameVersionNotInstalledException(string message)
        : base(message)
    {
    }

    public GameVersionNotInstalledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Construit l'exception pour une version précise, avec un message actionnable.</summary>
    public static GameVersionNotInstalledException For(GameVersion version)
        => new($"La version {version} n'est pas installée. Installe-la avant de lancer cette instance.");
}