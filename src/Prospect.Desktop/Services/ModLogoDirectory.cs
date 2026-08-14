using Prospect.Core.ModDb;

namespace Prospect.Desktop.Services;

/// <inheritdoc cref="IModLogoDirectory" />
/// <remarks>
/// <para>
/// Trois décisions, et elles tiennent toutes à la même contrainte : ce service décore, il ne rend
/// aucun service que l'utilisateur ait demandé.
/// </para>
/// <para>
/// 1. Il lit le catalogue MÉMORISÉ (<see cref="IModDbClient.TryGetCachedCatalogAsync"/>) et jamais
/// le réseau. L'onglet Mods d'une instance n'émettait aucun appel à l'ouverture, il vit d'un scan
/// disque ; lui faire télécharger trois mégaoctets et demi de catalogue pour décorer ses rangées
/// serait un échange perdant, et un échange que l'utilisateur n'a pas demandé. La conséquence est
/// assumée : une installation dont le catalogue n'a jamais été relevé n'affiche aucune vignette
/// ailleurs que dans le navigateur, jusqu'à la première ouverture de celui-ci.
/// </para>
/// <para>
/// 2. La table est construite UNE FOIS par session, et l'échec ne se réessaie pas non plus. Elle
/// pèse une entrée par fiche à logo (5 433 des 8 026 du catalogue réel, soit de l'ordre de 500 Kio
/// de dictionnaire et d'<see cref="Uri"/>), à comparer aux 3,5 Mio du document de catalogue que
/// <c>ModDbClient</c> garde de toute façon en mémoire dès qu'il l'a lu une fois. Rafraîchir cette
/// table à chaque ouverture d'écran coûterait une désérialisation complète pour une donnée qui ne
/// bouge pas à l'heure.
/// </para>
/// <para>
/// 3. Rien ne lève, jamais. <see cref="ModDbUnavailableException"/> est déjà exclue par le contrat
/// de la lecture en cache, mais le filtre reste large : un appelant qui décore une rangée n'a pas
/// de branche d'échec, et une vignette absente est un état parfaitement lisible.
/// </para>
/// </remarks>
public sealed class ModLogoDirectory : IModLogoDirectory
{
    private readonly Lazy<Task<ModLogoIndex>> _index;

    /// <summary>Construit l'annuaire.</summary>
    /// <param name="client">Client ModDB, interrogé pour son seul cache.</param>
    /// <remarks>
    /// Le CONSTRUIRE ne lit rien, pas même le cache disque : même invariant que
    /// <c>ThemeService</c> et <c>BackdropService</c>, parce qu'il est traversé par le graphe DI de
    /// tout test qui résout la fenêtre. La table se bâtit à la première rangée qui la demande.
    /// </remarks>
    public ModLogoDirectory(IModDbClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        // Lazy plutôt qu'un verrou : une liste de vingt mods construite d'un coup demanderait
        // sinon vingt fois le même catalogue, dix-neuf désérialisations étant jetées par la
        // vingtième. Ici la première demande lance la construction et les autres attendent la
        // même tâche. Le jeton de l'APPELANT n'entre pas dedans (voir FindAsync) : une rangée qui
        // s'annule ne doit pas empoisonner la table de toutes les autres.
        _index = new Lazy<Task<ModLogoIndex>>(() => BuildAsync(client), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public async Task<Uri?> FindAsync(int modDbModId, CancellationToken cancellationToken = default)
    {
        if (modDbModId <= 0)
        {
            return null;
        }

        var index = await _index.Value.WaitAsync(cancellationToken).ConfigureAwait(false);

        return index.Find(modDbModId);
    }

    private static async Task<ModLogoIndex> BuildAsync(IModDbClient client)
    {
        try
        {
            var catalog = await client.TryGetCachedCatalogAsync(CancellationToken.None).ConfigureAwait(false);

            return catalog is null ? ModLogoIndex.Empty : ModLogoIndex.Build(catalog.Mods);
        }
        catch (Exception)
        {
            // Le contrat de TryGetCachedCatalogAsync exclut déjà les pannes attendues ; ce filet
            // couvre l'inattendu, pour qu'une rangée décorative ne puisse en aucun cas faire
            // tomber l'écran qui la porte. Le résultat vide est mémorisé comme les autres :
            // réessayer coûterait une tentative par mod pour le même échec.
            return ModLogoIndex.Empty;
        }
    }
}