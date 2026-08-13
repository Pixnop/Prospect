using System.Collections.Concurrent;

using Avalonia;
using Avalonia.Media.Imaging;

namespace Prospect.Desktop.Services;

/// <inheritdoc cref="IModLogoCache" />
/// <remarks>
/// Quatre décisions, toutes dictées par ce que le catalogue RÉEL impose et qu'un double à deux mods
/// ne pouvait pas révéler : 8 026 fiches, dont 5 433 avec un logo, servies en pleine résolution CDN
/// (480x320 et au-delà) par un endpoint sans pagination (docs/research/moddb-api.md).
///
/// 1. <see cref="_slots"/> borne le nombre de téléchargements simultanés : sans lui, une liste de
///    plusieurs centaines de résultats déclencherait autant de requêtes HTTP d'un coup.
/// 2. Une vignette plus large que <see cref="MaxLogoWidth"/> est RÉDUITE à cette largeur, et
///    l'image pleine résolution est libérée aussitôt. C'est la correction qui compte le plus : une
///    carte montre son logo dans 40 points de côté, alors qu'une image du CDN décodée telle quelle
///    occupe de l'ordre de 600 Ko de pixels. Mesuré sur le catalogue réel, garder la pleine
///    résolution faisait monter le jeu de travail à près de 8 Gio pour 5 333 logos, c'est-à-dire une
///    mort par épuisement mémoire sur une machine ordinaire. Une image déjà plus petite n'est jamais
///    agrandie : la réduction est un plafond, pas une normalisation.
/// 3. Le cache est BORNÉ (<see cref="MaxCachedBitmaps"/>). Au-delà, un logo est toujours servi mais
///    n'est plus mémorisé : un plafond franc vaut mieux qu'une éviction, qui reviendrait à libérer
///    un bitmap peut-être encore accroché à un <c>Image</c> de l'arbre visuel (voir point 4).
/// 4. <see cref="Dispose"/> ne libère PAS les bitmaps distribués. Ils appartiennent de fait aux
///    contrôles qui les affichent : un <c>Image.Source</c> qui pointe vers un <see cref="Bitmap"/>
///    libéré fait lever une <see cref="NullReferenceException"/> à la passe de mise en page
///    suivante, dans <c>Avalonia.Controls.Image.MeasureOverride</c> (<c>Bitmap.Size</c> déréférence
///    un <c>PlatformImpl.Item</c> devenu nul). Ce cas est atteignable en production : <c>App</c>
///    libère le conteneur sur <c>ShutdownRequested</c>, alors que la fenêtre principale est encore
///    vivante et peut encore se remettre en page.
///
/// Seuls les décodages RÉUSSIS sont mémorisés ; un échec n'est jamais retenu, pour qu'un blocage
/// réseau passager ne condamne pas un mod au pictogramme générique pour le reste de la session.
/// </remarks>
public sealed class ModLogoCache : IModLogoCache, IDisposable
{
    private const int MaxConcurrentFetches = 4;

    /// <summary>
    /// Largeur maximale conservée, en pixels. La carte affiche le logo dans un carré de 40 points
    /// de côté (ModBrowserView.axaml) : 128 px couvre confortablement un écran à 200 % sans jamais
    /// garder la résolution d'origine du CDN.
    /// </summary>
    public const int MaxLogoWidth = 128;

    /// <summary>
    /// Nombre maximal de vignettes mémorisées. Volontairement bien au-dessus de ce qu'une session
    /// de navigation réaliste atteint (la grille ne rend qu'une fenêtre de cartes à la fois, voir
    /// <c>ModBrowserViewModel</c>), et assez bas pour que le pire cas reste de l'ordre de quelques
    /// dizaines de mégaoctets plutôt que de plusieurs gigaoctets.
    /// </summary>
    public const int MaxCachedBitmaps = 512;

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

    /// <summary>Nombre de vignettes actuellement mémorisées. Exposé pour que les tests vérifient la borne.</summary>
    public int CachedCount => _cache.Count;

    /// <inheritdoc />
    public Task<Bitmap?> GetAsync(Uri logoUrl, CancellationToken cancellationToken = default)
        => GetAsync(logoUrl, MaxLogoWidth, cancellationToken);

    /// <inheritdoc />
    public async Task<Bitmap?> GetAsync(Uri imageUrl, int maxWidth, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageUrl);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);

        // La largeur d'usage fait partie de la CLÉ : la même illustration peut être demandée en
        // vignette de 128 px par une carte et en 640 px par une fiche, et servir la première à la
        // seconde donnerait une image floue étirée.
        var key = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{maxWidth}|{imageUrl.AbsoluteUri}");
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        byte[] bytes;
        if (!await TryEnterAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            // Un autre appelant a pu peupler l'entrée pendant l'attente d'un créneau.
            if (_cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            bytes = await _httpClient.GetByteArrayAsync(imageUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRecoverableFetchFailure(exception, cancellationToken))
        {
            return null;
        }
        finally
        {
            Release();
        }

        return Decode(key, bytes, maxWidth);
    }

    /// <summary>
    /// Oublie les vignettes mémorisées et libère le compteur de créneaux. Ne libère PAS les bitmaps
    /// déjà distribués : voir le point 4 de la remarque de classe.
    /// </summary>
    public void Dispose()
    {
        _cache.Clear();
        _slots.Dispose();
    }

    // Bloc try/catch large exprès : un flux reçu mais illisible comme image ne doit jamais remonter
    // jusqu'à la carte, quelle qu'en soit la raison exacte (format non supporté, contenu tronqué...).
    private Bitmap? Decode(string key, byte[] bytes, int maxWidth)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = Shrink(new Bitmap(stream), maxWidth);

            if (_cache.Count < MaxCachedBitmaps)
            {
                _cache[key] = bitmap;
            }

            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Réduit une vignette trop large et libère l'originale, qui n'a jamais quitté cette méthode :
    // c'est le seul bitmap que ce cache ait le droit de libérer (voir point 4 de la remarque).
    private static Bitmap Shrink(Bitmap decoded, int maxWidth)
    {
        var size = decoded.PixelSize;
        if (size.Width <= maxWidth)
        {
            return decoded;
        }

        var height = Math.Max(1, (int)Math.Round(size.Height * (double)maxWidth / size.Width));
        try
        {
            return decoded.CreateScaledBitmap(new PixelSize(maxWidth, height), BitmapInterpolationMode.HighQuality);
        }
        finally
        {
            decoded.Dispose();
        }
    }

    // Rend faux quand le créneau n'a pas pu être pris parce que le cache est déjà fermé (arrêt de
    // l'application). Une annulation demandée par l'appelant, elle, ressort intacte : c'est le
    // contrat documenté sur IModLogoCache.GetAsync.
    private async Task<bool> TryEnterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void Release()
    {
        try
        {
            _slots.Release();
        }
        catch (ObjectDisposedException)
        {
            // Cache fermé pendant qu'un téléchargement était en vol : plus rien à rendre.
        }
    }

    // Même piège que ModDbClient.IsNetworkFailure : HttpClient lève la même TaskCanceledException
    // pour son propre délai interne que pour une annulation demandée par l'appelant. Seul l'état du
    // jeton reçu permet de trancher : s'il est déclenché, c'est une décision de l'appelant, à
    // laisser remonter intacte ; sinon, c'est une panne réseau comme une autre.
    private static bool IsRecoverableFetchFailure(Exception exception, CancellationToken callerToken)
        => exception switch
        {
            OperationCanceledException => !callerToken.IsCancellationRequested,
            HttpRequestException or IOException or ObjectDisposedException => true,
            _ => false,
        };
}