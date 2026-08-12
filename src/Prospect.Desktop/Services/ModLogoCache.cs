using System.Collections.Concurrent;

using Avalonia.Media.Imaging;

namespace Prospect.Desktop.Services;

/// <inheritdoc cref="IModLogoCache" />
/// <remarks>
/// Trois décisions, chacune dictée par ce que docs/research/moddb-api.md dit du champ <c>logo</c>
/// (absent sur 30 à 40 % des mods, jamais de taille ni de checksum) et par la grille de résultats
/// du navigateur, qui n'est pas virtualisée (<c>WrapPanel</c> dans un simple <c>ItemsControl</c>) :
/// toutes les cartes d'une recherche se matérialisent d'un coup, logo compris.
///
/// 1. <see cref="_slots"/> borne le nombre de téléchargements de logos simultanés : sans lui, un
///    catalogue de plusieurs centaines de résultats déclencherait autant de requêtes HTTP en
///    parallèle au premier rendu de la liste.
/// 2. Seuls les décodages RÉUSSIS sont mis en cache ; un échec n'est jamais mémorisé, pour qu'un
///    blocage réseau passager ne condamne pas un mod au pictogramme générique pour le reste de la
///    session.
/// 3. Le décodage (<see cref="Bitmap"/>) est isolé dans son propre bloc <c>try/catch</c>, large
///    exprès : un flux reçu mais illisible comme image ne doit jamais remonter jusqu'à la carte,
///    quelle qu'en soit la raison exacte (format non supporté, contenu tronqué...).
/// </remarks>
public sealed class ModLogoCache : IModLogoCache, IDisposable
{
    private const int MaxConcurrentFetches = 4;

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, Bitmap> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _slots = new(MaxConcurrentFetches, MaxConcurrentFetches);

    /// <summary>Construit le cache.</summary>
    /// <param name="httpClient">
    /// Client HTTP dédié (voir CompositionRoot) : un délai de requête modéré suffit, une vignette
    /// décorative ne mérite pas le budget de temps d'un appel de catalogue.
    /// </param>
    public ModLogoCache(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<Bitmap?> GetAsync(Uri logoUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logoUrl);

        var key = logoUrl.AbsoluteUri;
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        byte[] bytes;
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Un autre appelant a pu peupler l'entrée pendant l'attente d'un créneau.
            if (_cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            bytes = await _httpClient.GetByteArrayAsync(logoUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRecoverableFetchFailure(exception, cancellationToken))
        {
            return null;
        }
        finally
        {
            _slots.Release();
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            _cache[key] = bitmap;

            return bitmap;
        }
        catch (Exception)
        {
            // Contenu reçu mais pas une image décodable (format inattendu, flux tronqué...) :
            // repli silencieux sur le pictogramme générique, jamais une exception qui remonterait
            // jusqu'à la carte. Voir la remarque de la classe, point 3.
            return null;
        }
    }

    /// <summary>
    /// Libère le sémaphore et les bitmaps décodés (ressources natives Skia, mieux valant les
    /// libérer explicitement à la fermeture de l'application qu'attendre un finaliseur).
    /// </summary>
    public void Dispose()
    {
        foreach (var bitmap in _cache.Values)
        {
            bitmap.Dispose();
        }

        _slots.Dispose();
    }

    // Même piège que ModDbClient.IsNetworkFailure : HttpClient lève la même TaskCanceledException
    // pour son propre délai interne que pour une annulation demandée par l'appelant. Seul l'état du
    // jeton reçu permet de trancher : s'il est déclenché, c'est une décision de l'appelant, à
    // laisser remonter intacte ; sinon, c'est une panne réseau comme une autre.
    private static bool IsRecoverableFetchFailure(Exception exception, CancellationToken callerToken)
        => exception switch
        {
            OperationCanceledException => !callerToken.IsCancellationRequested,
            HttpRequestException or IOException => true,
            _ => false,
        };
}