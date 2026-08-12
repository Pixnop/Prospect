namespace Prospect.Core.Auth;

/// <summary>
/// Le service d'authentification n'a pas pu être joint, ou a répondu quelque chose d'illisible.
/// Distinguée d'un refus (<see cref="VsLoginStatus.InvalidEmailOrPassword"/> et compagnie) pour la
/// même raison que <c>ModDbUnavailableException</c> l'est de <c>ModDbApiException</c> : un refus
/// demande de corriger sa saisie, une coupure demande de réessayer plus tard.
/// </summary>
public sealed class VsAccountUnavailableException : Exception
{
    private VsAccountUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Construit l'erreur à partir de la panne de transport qui l'a causée.</summary>
    public static VsAccountUnavailableException FromNetworkFailure(Exception innerException)
        => new("Le service de compte Vintage Story est injoignable.", innerException);
}