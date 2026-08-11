using System.Net;

namespace Prospect.Core.Http;

/// <summary>
/// Décide si un échec réseau vaut la peine d'être retenté ou de faire basculer sur le miroir
/// suivant. Un seul endroit dans le Core connaît cette liste, pour que le catalogue et les
/// téléchargements aient exactement la même définition de « transitoire ».
/// </summary>
public static class TransientHttpFailure
{
    /// <summary>
    /// Vrai pour les pannes de transport (connexion refusée, DNS, coupure en cours de flux) et
    /// les délais dépassés. Une annulation demandée par l'appelant n'est jamais transitoire :
    /// c'est une décision, pas une panne.
    /// </summary>
    public static bool IsTransient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            OperationCanceledException => false,
            HttpRequestException httpRequest => IsTransientStatus(httpRequest.StatusCode),
            TimeoutException => true,
            IOException => true,
            _ => false,
        };
    }

    /// <summary>
    /// Vrai pour les codes que le serveur peut servir correctement à la tentative suivante :
    /// absence de code (échec de transport), 408, 429 et toute la famille 5xx. Un 404 ou un 403
    /// sont au contraire définitifs, insister ne changerait rien.
    /// </summary>
    public static bool IsTransientStatus(HttpStatusCode? statusCode) => statusCode switch
    {
        null => true,
        HttpStatusCode.RequestTimeout => true,
        HttpStatusCode.TooManyRequests => true,
        var code => (int)code >= 500,
    };
}