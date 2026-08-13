using Prospect.Core.Instances;

namespace Prospect.Core.Launching;

/// <summary>
/// macOS : refus explicite plutôt qu'une tentative de lancement. Le téléchargement et
/// l'installation mac sont traités comme une cible réelle depuis la PR 5
/// (docs/architecture.md, section « 2. Versions du jeu »), mais le bouton Jouer attend une vraie
/// machine de test avant d'être activé, pour ne pas reproduire l'erreur de VS Launcher : des
/// années de « not yet supported » silencieux, sans avoir anticipé la question
/// (docs/research/vslauncher-et-distribution.md, implication 12). Le message honnête ici, plutôt
/// qu'un échec générique de lancement de processus, est ce que l'UI affiche directement à
/// l'utilisateur mac.
/// </summary>
public sealed class MacGameLaunchStrategy : IGameLaunchStrategy
{
    /// <inheritdoc />
    /// <exception cref="MacLaunchNotSupportedException">Toujours : voir la remarque de la classe.</exception>
    public string ResolveExecutablePath(string installDirectory)
        => throw new MacLaunchNotSupportedException(
            "Le lancement du jeu sur macOS n'est pas encore pris en charge par Prospect. Le téléchargement et l'installation fonctionnent déjà ; le bouton Jouer attend une machine de test.");

    /// <inheritdoc />
    /// <remarks>
    /// Rien à ajouter : <c>mesa_glthread</c> est une option des pilotes Mesa, absents de macOS. Ce
    /// membre ne lève pas, contrairement à <see cref="ResolveExecutablePath"/> : construire un
    /// environnement n'engage aucun lancement, et une réponse honnête vaut mieux qu'une exception
    /// à un endroit où il n'y a rien d'impossible.
    /// </remarks>
    public IReadOnlyDictionary<string, string> BuildEnvironment(InstanceLaunchSettings launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        return launch.Env;
    }
}