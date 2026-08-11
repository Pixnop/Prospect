namespace Prospect.Core.ModDb;

/// <summary>
/// L'API a répondu, mais avec un échec applicatif : <c>statuscode</c> différent de <c>"200"</c>
/// sur v1, ou vrai code HTTP d'erreur sur v2. Distinguée d'une panne réseau
/// (<see cref="ModDbUnavailableException"/>) parce que les deux n'appellent pas la même réaction :
/// une fiche introuvable est définitive, une coupure réseau se réessaie.
/// </summary>
public sealed class ModDbApiException : Exception
{
    private ModDbApiException(string message, string statusCode)
        : base(message) => StatusCode = statusCode;

    /// <summary>Code renvoyé, tel quel : chaîne JSON en v1, code HTTP en chaîne en v2.</summary>
    public string StatusCode { get; }

    /// <summary>Vrai si l'échec signifie « cette ressource n'existe pas ».</summary>
    public bool IsNotFound => StatusCode is "404";

    /// <summary>Échec applicatif d'un endpoint v1, lu dans le corps JSON et non dans le code HTTP.</summary>
    public static ModDbApiException FromV1StatusCode(string endpoint, string? statusCode)
        => new(
            $"Le ModDB a répondu {statusCode ?? "sans code"} sur {endpoint} (le code HTTP de l'API v1 ne reflète pas l'erreur).",
            statusCode ?? "inconnu");

    /// <summary>Échec d'un endpoint v2, dont les codes HTTP sont, eux, fiables.</summary>
    public static ModDbApiException FromV2StatusCode(string endpoint, int statusCode)
        => new(
            $"Le ModDB a répondu HTTP {statusCode} sur {endpoint}.",
            statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// Le ModDB n'a pas pu être joint et aucun cache ne permettait de servir la demande. L'écran de
/// navigation en fait le bandeau « ModDB injoignable » de la maquette plutôt qu'une erreur
/// bloquante : les mods déjà installés restent utilisables hors ligne.
/// </summary>
public sealed class ModDbUnavailableException : Exception
{
    private ModDbUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Construit l'erreur à partir de la panne réseau qui l'a causée.</summary>
    public static ModDbUnavailableException FromNetworkFailure(Exception innerException)
        => new("Le ModDB est injoignable et aucun relevé en cache n'est disponible.", innerException);
}