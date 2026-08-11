namespace Prospect.Core.Http;

/// <summary>
/// Paramètres d'une politique de réessai : nombre de tentatives et backoff exponentiel entre
/// elles. Value object immuable, partagé par le client du catalogue et par le moteur de
/// téléchargement (docs/architecture.md, « Transverse : téléchargements »).
/// </summary>
/// <param name="MaxAttempts">Nombre total de tentatives, la première comprise. Toujours au moins 1.</param>
/// <param name="InitialDelay">Attente avant la deuxième tentative.</param>
/// <param name="BackoffFactor">Facteur multiplicatif appliqué à l'attente à chaque nouvelle tentative.</param>
public sealed record RetryOptions(int MaxAttempts, TimeSpan InitialDelay, double BackoffFactor)
{
    /// <summary>
    /// Réglage par défaut : trois tentatives espacées de 1 s puis 2 s. Volontairement court, un
    /// appelant bloqué derrière un réseau coupé doit obtenir sa réponse « indisponible » vite
    /// plutôt que de faire attendre l'interface une minute.
    /// </summary>
    public static RetryOptions Default { get; } = new(3, TimeSpan.FromSeconds(1), 2d);

    /// <summary>
    /// Réglage sans attente, réservé aux tests : la logique de réessai est exercée, l'horloge ne
    /// l'est pas.
    /// </summary>
    public static RetryOptions NoDelay { get; } = new(3, TimeSpan.Zero, 1d);

    /// <summary>
    /// Attente avant la tentative d'indice <paramref name="attemptIndex"/> (0 pour la première,
    /// qui ne patiente jamais).
    /// </summary>
    public TimeSpan DelayBeforeAttempt(int attemptIndex)
    {
        if (attemptIndex <= 0)
        {
            return TimeSpan.Zero;
        }

        var multiplier = Math.Pow(BackoffFactor, attemptIndex - 1);

        return InitialDelay * multiplier;
    }
}