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
/// Port vers l'exécution de processus externes. Le Core ne démarre jamais un processus
/// directement : c'est ce qui rend testable l'installeur Inno de Windows depuis une machine Linux,
/// et ce qui servira aussi au lancement du jeu.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Démarre le processus, attend sa fin, rend son code de sortie et ses sorties.</summary>
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default);
}