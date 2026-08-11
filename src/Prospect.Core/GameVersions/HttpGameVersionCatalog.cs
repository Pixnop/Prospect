using System.Text.Json;

using Prospect.Core.Common;
using Prospect.Core.Http;
using Prospect.Core.Storage;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Implémentation d'<see cref="IGameVersionCatalog"/> adossée à l'API publique
/// <c>api.vintagestory.at</c> (qualifiée de publique par ses propres mainteneurs, aucun
/// en-tête d'authentification n'est envoyé : docs/research/vslauncher-et-distribution.md,
/// sections b et c).
/// </summary>
/// <remarks>
/// Trois niveaux, du moins cher au plus cher : un cache mémoire pour la durée de la session, un
/// cache disque dans <c>cache/http/</c> avec un TTL long (le catalogue bouge de quelques versions
/// par mois, pas par heure), puis le réseau. Le repli hors-ligne est explicite : si l'API ne
/// répond pas, un cache périmé est servi et signalé comme tel via
/// <see cref="GameCatalogFreshness.Stale"/>, plutôt que de laisser l'écran Versions vide.
/// </remarks>
public sealed class HttpGameVersionCatalog : IGameVersionCatalog, IDisposable
{
    /// <summary>Document des versions finales.</summary>
    public static readonly Uri StableUrl = new("https://api.vintagestory.at/stable.json");

    /// <summary>Document des versions candidates et pre-releases.</summary>
    public static readonly Uri UnstableUrl = new("https://api.vintagestory.at/unstable.json");

    private const string CacheFileName = "game-versions.json";

    private readonly HttpClient _httpClient;
    private readonly JsonFileStore _jsonFileStore;
    private readonly AppPaths _appPaths;
    private readonly IClock _clock;
    private readonly RetryPolicy _retryPolicy;
    private readonly TimeSpan _timeToLive;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private GameVersionCatalog? _memoryCache;

    /// <summary>
    /// Construit le client.
    /// </summary>
    /// <param name="httpClient">Client HTTP dédié au catalogue (délai de requête normal, contrairement à celui des téléchargements).</param>
    /// <param name="jsonFileStore">Écritures atomiques du cache disque.</param>
    /// <param name="appPaths">Emplacement de <c>cache/http/</c>.</param>
    /// <param name="clock">Horloge, pour dater le cache et évaluer son TTL.</param>
    /// <param name="retryPolicy">Politique de réessai des appels réseau. <see cref="RetryOptions.Default"/> si omise.</param>
    /// <param name="timeToLive">Durée de validité du cache disque. Six heures par défaut.</param>
    public HttpGameVersionCatalog(
        HttpClient httpClient,
        JsonFileStore jsonFileStore,
        AppPaths appPaths,
        IClock clock,
        RetryPolicy? retryPolicy = null,
        TimeSpan? timeToLive = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(jsonFileStore);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(clock);

        _httpClient = httpClient;
        _jsonFileStore = jsonFileStore;
        _appPaths = appPaths;
        _clock = clock;
        _retryPolicy = retryPolicy ?? new RetryPolicy(RetryOptions.Default);
        _timeToLive = timeToLive ?? TimeSpan.FromHours(6);
    }

    /// <summary>Chemin du fichier de cache disque.</summary>
    public string CacheFilePath => Path.Combine(_appPaths.HttpCacheDirectory, CacheFileName);

    /// <summary>Libère le verrou qui sérialise les consultations concurrentes.</summary>
    public void Dispose() => _mutex.Dispose();

    /// <inheritdoc />
    public async Task<GameVersionCatalog> GetAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await GetCoreAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<GameVersionCatalog> GetCoreAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _memoryCache is { } cached && IsFresh(cached.RetrievedUtc))
        {
            return cached with { Freshness = GameCatalogFreshness.Cached };
        }

        var onDisk = await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
        if (!forceRefresh && onDisk is not null && IsFresh(onDisk.RetrievedUtc))
        {
            var fromCache = Convert(onDisk, GameCatalogFreshness.Cached);
            if (fromCache is not null)
            {
                _memoryCache = fromCache;
                return fromCache;
            }
        }

        try
        {
            return await FetchAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException or JsonException)
        {
            var stale = onDisk is null ? null : Convert(onDisk, GameCatalogFreshness.Stale);

            return stale ?? throw GameCatalogUnavailableException.FromNetworkFailure(exception);
        }
    }

    private async Task<GameVersionCatalog> FetchAsync(CancellationToken cancellationToken)
    {
        var stableJson = await GetStringAsync(StableUrl, cancellationToken).ConfigureAwait(false);
        var unstableJson = await GetStringAsync(UnstableUrl, cancellationToken).ConfigureAwait(false);

        var versions = GameCatalogParser.Merge(stableJson, unstableJson);
        var retrievedUtc = _clock.UtcNow;

        await WriteCacheAsync(new GameCatalogCacheDocument(
            GameCatalogCacheDocument.CurrentSchemaVersion,
            retrievedUtc,
            stableJson,
            unstableJson), cancellationToken).ConfigureAwait(false);

        var catalog = new GameVersionCatalog(versions, retrievedUtc, GameCatalogFreshness.Live);
        _memoryCache = catalog;

        return catalog;
    }

    private Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken)
        => _retryPolicy.ExecuteAsync(
            async (_, token) =>
            {
                using var response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            },
            TransientHttpFailure.IsTransient,
            cancellationToken);

    private bool IsFresh(DateTimeOffset retrievedUtc)
    {
        var age = _clock.UtcNow - retrievedUtc;

        return age >= TimeSpan.Zero && age < _timeToLive;
    }

    private static GameVersionCatalog? Convert(GameCatalogCacheDocument document, GameCatalogFreshness freshness)
    {
        try
        {
            return new GameVersionCatalog(
                GameCatalogParser.Merge(document.StableJson, document.UnstableJson),
                document.RetrievedUtc,
                freshness);
        }
        catch (JsonException)
        {
            // Un cache illisible n'est pas une erreur remontable : c'est un cache, on l'ignore.
            return null;
        }
    }

    private async Task<GameCatalogCacheDocument?> ReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var document = await _jsonFileStore
                .ReadAsync(CacheFilePath, GameVersionsJsonContext.Default.GameCatalogCacheDocument, cancellationToken)
                .ConfigureAwait(false);

            return document?.SchemaVersion == GameCatalogCacheDocument.CurrentSchemaVersion ? document : null;
        }
        catch (CorruptedFileException)
        {
            return null;
        }
    }

    private async Task WriteCacheAsync(GameCatalogCacheDocument document, CancellationToken cancellationToken)
    {
        try
        {
            await _jsonFileStore
                .WriteAsync(CacheFilePath, document, GameVersionsJsonContext.Default.GameCatalogCacheDocument, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Cache non écrit (disque plein, dossier en lecture seule) : la réponse réseau reste
            // parfaitement valide, ce serait absurde de la jeter pour autant.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}