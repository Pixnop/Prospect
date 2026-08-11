namespace Prospect.Core.Http;

/// <summary>
/// Réessai avec backoff exponentiel autour d'une opération réseau. Écrit à la main plutôt que
/// délégué à <c>Microsoft.Extensions.Http.Resilience</c> : le chemin de téléchargement a besoin
/// d'un délai d'inactivité par lecture et d'un basculement de miroir, deux notions qu'un
/// gestionnaire de résilience standard modélise mal (il impose au contraire un délai total par
/// requête, rédhibitoire pour un fichier de 600 Mo), et une seule mécanique de réessai pour le
/// catalogue et pour les téléchargements vaut mieux que deux.
/// </summary>
public sealed class RetryPolicy
{
    private readonly RetryOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>
    /// Construit la politique.
    /// </summary>
    /// <param name="options">Nombre de tentatives et backoff.</param>
    /// <param name="delay">
    /// Fonction d'attente, <see cref="Task.Delay(TimeSpan, CancellationToken)"/> par défaut.
    /// Injectable pour que les tests exercent la mécanique de réessai sans attendre réellement.
    /// </param>
    public RetryPolicy(RetryOptions options, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>
    /// Exécute <paramref name="operation"/> jusqu'à <see cref="RetryOptions.MaxAttempts"/> fois
    /// tant qu'elle échoue sur une exception jugée transitoire par <paramref name="isTransient"/>.
    /// La dernière exception est relancée telle quelle une fois les tentatives épuisées ; une
    /// exception non transitoire remonte immédiatement, sans réessai.
    /// </summary>
    /// <param name="operation">Opération à tenter, qui reçoit l'indice de tentative (0 pour la première).</param>
    /// <param name="isTransient">Prédicat décidant si l'échec vaut la peine d'être retenté.</param>
    /// <param name="cancellationToken">Annulation : jamais retentée, une annulation n'est pas un échec transitoire.</param>
    public async Task<T> ExecuteAsync<T>(
        Func<int, CancellationToken, Task<T>> operation,
        Func<Exception, bool> isTransient,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(isTransient);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt + 1 < _options.MaxAttempts && isTransient(exception))
            {
                await _delay(_options.DelayBeforeAttempt(attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}