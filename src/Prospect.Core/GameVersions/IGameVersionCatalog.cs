namespace Prospect.Core.GameVersions;

/// <summary>
/// Catalogue distant des versions du jeu (docs/architecture.md, « 2. Versions du jeu ») : la
/// fusion de <c>stable.json</c> et <c>unstable.json</c>, servie depuis le réseau ou depuis le
/// cache selon la fraîcheur et la disponibilité.
/// </summary>
public interface IGameVersionCatalog
{
    /// <summary>
    /// Rend le catalogue courant.
    /// </summary>
    /// <param name="forceRefresh">
    /// Ignore le cache et retente le réseau. C'est ce que déclenche « Vérifier les nouveautés »
    /// dans l'écran Versions ; un repli sur le cache périmé reste possible si le réseau ne
    /// répond pas.
    /// </param>
    /// <param name="cancellationToken">Annulation de l'appel.</param>
    /// <exception cref="GameCatalogUnavailableException">Réseau injoignable et aucun cache exploitable.</exception>
    Task<GameVersionCatalog> GetAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}