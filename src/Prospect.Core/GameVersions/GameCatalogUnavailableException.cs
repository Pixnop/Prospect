namespace Prospect.Core.GameVersions;

/// <summary>
/// Le catalogue distant est injoignable et aucun cache disque, même périmé, ne permet de
/// répondre. Erreur attendue du domaine, donc exception typée du projet plutôt qu'une
/// <see cref="Exception"/> nue (docs/architecture.md).
/// </summary>
public sealed class GameCatalogUnavailableException : Exception
{
    public GameCatalogUnavailableException()
        : base("Le catalogue des versions du jeu est injoignable et aucun cache local n'est disponible.")
    {
    }

    public GameCatalogUnavailableException(string message)
        : base(message)
    {
    }

    public GameCatalogUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Construit l'exception à partir de la panne réseau qui l'a provoquée.</summary>
    public static GameCatalogUnavailableException FromNetworkFailure(Exception innerException)
        => new("Le catalogue des versions du jeu est injoignable et aucun cache local n'est disponible.", innerException);
}