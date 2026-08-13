namespace Prospect.Core.Common;

/// <summary>
/// Un processus externe à lancer.
/// </summary>
/// <param name="FileName">Exécutable à démarrer.</param>
/// <param name="Arguments">
/// Arguments, un par élément. Volontairement une liste et non une chaîne collée : VS Launcher
/// passait les paramètres utilisateur comme un unique argv et c'était fragile
/// (docs/research/vslauncher-et-distribution.md, implication 9).
/// </param>
public sealed record ProcessRunRequest(string FileName, IReadOnlyList<string> Arguments);

/// <summary>
/// Ce qu'a produit un processus terminé.
/// </summary>
/// <param name="ExitCode">Code de sortie.</param>
/// <param name="StandardOutput">Sortie standard capturée.</param>
/// <param name="StandardError">Sortie d'erreur capturée.</param>
public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Requête de démarrage d'un processus long à suivre plutôt qu'à attendre (le jeu), à la
/// différence de <see cref="ProcessRunRequest"/> consommé par <see cref="IProcessRunner.RunAsync"/>
/// qui bloque jusqu'à la sortie. Porte en plus les variables d'environnement (utile par exemple
/// pour <c>mesa_glthread</c> sur Linux) et le dossier de travail (celui de l'installation du jeu),
/// dont un processus de jeu a besoin.
/// </summary>
public sealed record ProcessStartRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    string? WorkingDirectory = null);

/// <summary>
/// Un processus démarré via <see cref="IProcessRunner.Start"/>, dont le cycle de vie est suivi de
/// façon asynchrone plutôt qu'attendu en bloquant : c'est ce qui permet à <c>GameLauncher</c> de
/// revenir immédiatement à son appelant pendant que le jeu tourne, et à
/// <c>RunningInstanceTracker</c> d'observer sa sortie et de l'arrêter sur demande.
/// </summary>
public interface IRunningProcess : IDisposable
{
    /// <summary>Identifiant du processus système, pour affichage et diagnostic.</summary>
    int Id { get; }

    /// <summary>Une ligne de sortie standard vient d'être produite.</summary>
    event EventHandler<string>? OutputDataReceived;

    /// <summary>Une ligne de sortie d'erreur vient d'être produite.</summary>
    event EventHandler<string>? ErrorDataReceived;

    /// <summary>Attend la fin du processus et rend son code de sortie.</summary>
    Task<int> WaitForExitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Termine le processus. <paramref name="includeChildren"/> tue aussi son arbre de processus
    /// (le jeu peut détacher des sous-processus), vrai par défaut.
    /// </summary>
    void Kill(bool includeChildren = true);
}

/// <summary>
/// Port vers l'exécution de processus externes. Le Core ne démarre jamais un processus
/// directement : c'est ce qui rend testable l'installeur Inno de Windows depuis une machine Linux,
/// et c'est aussi ce qui sert au lancement du jeu.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Démarre le processus, attend sa fin, rend son code de sortie et ses sorties.</summary>
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Démarre un processus long sans attendre sa fin (contrairement à <see cref="RunAsync"/>) :
    /// utilisé pour le jeu, dont le cycle de vie doit rester observable pendant qu'il tourne.
    /// </summary>
    IRunningProcess Start(ProcessStartRequest request);
}