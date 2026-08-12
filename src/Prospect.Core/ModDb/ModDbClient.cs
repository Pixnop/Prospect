using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Prospect.Core.Common;
using Prospect.Core.Http;
using Prospect.Core.Storage;

namespace Prospect.Core.ModDb;

/// <summary>
/// Client REST typé du ModDB (docs/research/moddb-api.md). Même philosophie de cache que
/// <see cref="Prospect.Core.GameVersions.HttpGameVersionCatalog"/> : mémoire pour la session,
/// disque dans <c>cache/http/</c> avec un TTL court, puis réseau, et repli explicite sur un cache
/// périmé signalé <see cref="ModDbFreshness.Stale"/> quand l'API ne répond pas.
/// </summary>
/// <remarks>
/// Trois pièges de cette API sont traités ici et nulle part ailleurs. Sur v1, le code HTTP ne
/// reflète jamais l'erreur applicative : <see cref="ReadV1Async"/> désérialise le corps AVANT de
/// regarder <c>IsSuccessStatusCode</c> et lit le champ <c>statuscode</c>, une chaîne. Sur v2, les
/// codes HTTP sont fiables et sont donc vérifiés normalement. Enfin, chaque requête part avec un
/// <c>User-Agent</c> identifiable : la recherche n'a trouvé aucun rate limiting applicatif, ce qui
/// est une raison de plus d'être reconnaissable côté serveur plutôt que de se fondre dans le
/// trafic anonyme.
/// </remarks>
public sealed class ModDbClient : IModDbClient, IDisposable
{
    /// <summary>Base de l'API, sans slash final.</summary>
    public static readonly Uri ApiBaseUrl = new("https://mods.vintagestory.at/api/");

    private const string CacheFileName = "moddb-catalog.json";

    private readonly HttpClient _httpClient;
    private readonly JsonFileStore _jsonFileStore;
    private readonly AppPaths _appPaths;
    private readonly IClock _clock;
    private readonly RetryPolicy _retryPolicy;
    private readonly TimeSpan _timeToLive;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly Dictionary<string, ModDbCompatibilityIndex> _compatibilityCache = [];

    private ModDbCacheDocument? _memoryCache;

    /// <summary>
    /// Construit le client.
    /// </summary>
    /// <param name="httpClient">Client HTTP dédié aux appels d'API courts (délai de requête normal).</param>
    /// <param name="jsonFileStore">Écritures atomiques du cache disque.</param>
    /// <param name="appPaths">Emplacement de <c>cache/http/</c>.</param>
    /// <param name="clock">Horloge, pour dater le cache et évaluer son TTL.</param>
    /// <param name="retryPolicy">Politique de réessai. <see cref="RetryOptions.Default"/> si omise.</param>
    /// <param name="timeToLive">
    /// Durée de validité du cache. Une heure par défaut : le catalogue bouge plusieurs fois par
    /// jour, bien plus vite que celui des versions du jeu.
    /// </param>
    public ModDbClient(
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
        _timeToLive = timeToLive ?? TimeSpan.FromHours(1);
    }

    /// <summary>
    /// En-tête <c>User-Agent</c> envoyé sur chaque requête, du type <c>Prospect/0.1.0-dev</c> :
    /// un client desktop anonyme doit au minimum se nommer. La valeur elle-même vit dans
    /// <see cref="ProspectUserAgent"/> depuis que le client du compte Vintage Story a le même
    /// besoin, ce nom-ci reste le point d'entrée historique du domaine ModDB.
    /// </summary>
    public static string UserAgent => ProspectUserAgent.Value;

    /// <summary>Chemin du fichier de cache disque du catalogue et des tags.</summary>
    public string CacheFilePath => Path.Combine(_appPaths.HttpCacheDirectory, CacheFileName);

    /// <summary>Libère le verrou qui sérialise les consultations concurrentes.</summary>
    public void Dispose() => _mutex.Dispose();

    /// <inheritdoc />
    public async Task<ModDbCatalog> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var (document, freshness) = await GetCacheDocumentAsync(forceRefresh, cancellationToken).ConfigureAwait(false);

        return new ModDbCatalog(ModDbMapper.ToSummaries(document.Mods), document.RetrievedUtc, freshness);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModDbTag>> GetTagsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var (document, _) = await GetCacheDocumentAsync(forceRefresh, cancellationToken).ConfigureAwait(false);

        return ModDbMapper.ToTags(document.Tags);
    }

    /// <inheritdoc />
    public async Task<ModDbModDetail> GetModAsync(string modIdOrIdentifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modIdOrIdentifier);

        var endpoint = $"mod/{Uri.EscapeDataString(modIdOrIdentifier)}";

        try
        {
            var response = await ReadV1Async(endpoint, ModDbJsonContext.Default.ModDbModDetailResponseDto, cancellationToken).ConfigureAwait(false);

            return ModDbMapper.ToDetail(response.Mod)
                ?? throw ModDbApiException.FromV1StatusCode(endpoint, "404");
        }
        catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken))
        {
            // Sans ce filtre, une HttpRequestException (statut HTTP) ou une TaskCanceledException
            // (délai dépassé côté HttpClient) remontait brute jusqu'à l'appelant, alors que le
            // contrat documenté sur IModDbClient.GetModAsync promet ModDbUnavailableException :
            // même mécanique que GetUpdatesAsync.
            throw ModDbUnavailableException.FromNetworkFailure(exception);
        }
    }

    /// <inheritdoc />
    public async Task<ModDbCompatibilityIndex> GetCompatibilityIndexAsync(
        GameVersion gameVersion,
        bool widenToMinorSeries = false,
        CancellationToken cancellationToken = default)
    {
        // gv= exige une version complète, gameversion= exactement deux composantes : la regex du
        // site refuse l'inverse dans les deux sens.
        var query = widenToMinorSeries
            ? $"mods?gameversion={gameVersion.Major.ToString(CultureInfo.InvariantCulture)}.{gameVersion.Minor.ToString(CultureInfo.InvariantCulture)}"
            : $"mods?gv={Uri.EscapeDataString(gameVersion.ToString())}";

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_compatibilityCache.TryGetValue(query, out var cached))
            {
                return cached with { Freshness = ModDbFreshness.Cached };
            }

            var response = await ReadV1Async(query, ModDbJsonContext.Default.ModDbModListResponseDto, cancellationToken).ConfigureAwait(false);
            var index = new ModDbCompatibilityIndex(
                (response.Mods ?? []).Select(dto => dto.ModId).Where(modId => modId > 0).ToHashSet(),
                widenToMinorSeries,
                ModDbFreshness.Live);
            _compatibilityCache[query] = index;

            return index;
        }
        catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken))
        {
            // Sans index, l'écran de recherche montre les mods sans badge de compatibilité, ce qui
            // vaut infiniment mieux qu'un écran vide ou qu'un badge inventé.
            return ModDbCompatibilityIndex.Unavailable;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ModDbInstallInformation>> GetInstallInformationAsync(
        IReadOnlyList<string> identifiers,
        GameVersion gameVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifiers);

        var wanted = identifiers.Where(identifier => !string.IsNullOrWhiteSpace(identifier)).ToArray();
        if (wanted.Length == 0)
        {
            return new Dictionary<string, ModDbInstallInformation>(StringComparer.OrdinalIgnoreCase);
        }

        var query = "v2/mods/install-information"
            + $"?ids={Uri.EscapeDataString(string.Join(',', wanted))}"
            + $"&gv={Uri.EscapeDataString(gameVersion.ToString())}"
            + "&resolve-deps=true";

        try
        {
            var response = await ReadV2Async(query, ModDbJsonContext.Default.ModDbV2InstallInformationResponseDto, cancellationToken).ConfigureAwait(false);

            return BuildInstallInformation(response);
        }
        catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken) || exception is ModDbApiException)
        {
            // Ce croisement est un complément : la vérification locale du modinfo.json téléchargé
            // suffit à produire un plan de dépendances correct sans lui.
            return new Dictionary<string, ModDbInstallInformation>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ModDbRelease>> GetUpdatesAsync(
        IReadOnlyDictionary<string, ModVersion> installedMods,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installedMods);

        var entries = installedMods
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .Select(pair => $"{pair.Key}@{pair.Value}")
            .ToArray();

        if (entries.Length == 0)
        {
            return new Dictionary<string, ModDbRelease>(StringComparer.OrdinalIgnoreCase);
        }

        var endpoint = $"updates?mods={Uri.EscapeDataString(string.Join(',', entries))}";

        try
        {
            var response = await ReadV1Async(endpoint, ModDbJsonContext.Default.ModDbUpdatesResponseDto, cancellationToken).ConfigureAwait(false);

            return ModDbMapper.ToUpdates(response.Updates);
        }
        catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken))
        {
            // Contrairement au reste de l'API v1, /api/updates PEUT renvoyer un vrai code HTTP (400
            // sur une entrée malformée, docs/research/moddb-api.md) : EnsureSuccessStatusCode() le
            // lève alors comme n'importe quelle panne de transport, sans distinction possible à ce
            // niveau. Nos propres entrées sont toujours bien formées ; si ce cas se produit quand
            // même, mieux vaut le signaler comme une indisponibilité franche, conformément au
            // contrat documenté sur IModDbClient.GetUpdatesAsync, que laisser fuiter une
            // HttpRequestException que rien en amont n'attend.
            throw ModDbUnavailableException.FromNetworkFailure(exception);
        }
    }

    /// <inheritdoc />
    public async Task<long?> GetFileSizeAsync(Uri fileUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileUrl);

        try
        {
            using var message = CreateRequest(HttpMethod.Head, fileUrl);
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength : null;
        }
        catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken))
        {
            return null;
        }
    }

    private static Dictionary<string, ModDbInstallInformation> BuildInstallInformation(ModDbV2InstallInformationResponseDto response)
    {
        var result = new Dictionary<string, ModDbInstallInformation>(StringComparer.OrdinalIgnoreCase);
        var warnings = response.Warnings is null ? [] : response.Warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)).ToArray();

        // resolve-deps ajoute les dépendances transitives dans une clé à part : elles ne sont pas
        // dans `data`, qui ne contient que ce qui a été demandé explicitement.
        var resolved = (response.Resolved ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var (identifier, entry) in response.Data ?? [])
        {
            if (string.IsNullOrWhiteSpace(identifier) || entry.ErrorCode != 0)
            {
                continue;
            }

            var recommended = ModVersion.TryParse(entry.RecommendedUpgrade, out var version) ? version : (ModVersion?)null;
            result[identifier] = new ModDbInstallInformation(recommended, resolved, warnings);
        }

        // Une dépendance résolue par le serveur mais absente de `data` doit quand même remonter :
        // c'est précisément le cas intéressant pour un plan d'installation.
        foreach (var identifier in resolved.Where(identifier => !result.ContainsKey(identifier)))
        {
            result[identifier] = new ModDbInstallInformation(null, [], warnings);
        }

        return result;
    }

    private async Task<(ModDbCacheDocument Document, ModDbFreshness Freshness)> GetCacheDocumentAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _memoryCache is { } memory && IsFresh(memory.RetrievedUtc))
            {
                return (memory, ModDbFreshness.Cached);
            }

            var onDisk = await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
            if (!forceRefresh && onDisk is not null && IsFresh(onDisk.RetrievedUtc))
            {
                _memoryCache = onDisk;

                return (onDisk, ModDbFreshness.Cached);
            }

            try
            {
                return (await FetchCatalogAsync(cancellationToken).ConfigureAwait(false), ModDbFreshness.Live);
            }
            catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken))
            {
                var stale = onDisk ?? _memoryCache;

                return stale is not null
                    ? (stale, ModDbFreshness.Stale)
                    : throw ModDbUnavailableException.FromNetworkFailure(exception);
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    // Catalogue et tags sont relevés ensemble et cachés ensemble : les deux alimentent le même
    // écran, et les tags ne pèsent rien à côté des 3,5 Mo du catalogue.
    private async Task<ModDbCacheDocument> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        var mods = await ReadV1Async("mods", ModDbJsonContext.Default.ModDbModListResponseDto, cancellationToken).ConfigureAwait(false);
        var tags = await ReadV1Async("tags", ModDbJsonContext.Default.ModDbTagListResponseDto, cancellationToken).ConfigureAwait(false);

        var document = new ModDbCacheDocument
        {
            SchemaVersion = ModDbCacheDocument.CurrentSchemaVersion,
            RetrievedUtc = _clock.UtcNow,
            Mods = mods.Mods ?? [],
            Tags = tags.Tags ?? [],
        };

        _memoryCache = document;
        await WriteCacheAsync(document, cancellationToken).ConfigureAwait(false);

        return document;
    }

    /// <summary>
    /// Lit un endpoint v1 : désérialisation du corps D'ABORD, vérification de l'enveloppe ENSUITE.
    /// L'ordre est le cœur du contrat de cette API, où <c>fail()</c> n'appelle jamais
    /// <c>http_response_code()</c> et laisse donc PHP répondre 200 sur une erreur.
    /// </summary>
    private async Task<T> ReadV1Async<T>(string endpoint, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        where T : IModDbV1Envelope
    {
        var payload = await GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var document = Deserialize(payload, typeInfo, endpoint);

        return document.StatusCode == "200"
            ? document
            : throw ModDbApiException.FromV1StatusCode(endpoint, document.StatusCode);
    }

    /// <summary>Lit un endpoint v2, dont les codes HTTP sont fiables (<c>lib/api/v2.php</c> appelle bien <c>http_response_code()</c>).</summary>
    private async Task<T> ReadV2Async<T>(string endpoint, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        var payload = await _retryPolicy.ExecuteAsync(
            async (_, token) =>
            {
                using var message = CreateRequest(HttpMethod.Get, new Uri(ApiBaseUrl, endpoint));
                using var response = await _httpClient.SendAsync(message, token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw ModDbApiException.FromV2StatusCode(endpoint, (int)response.StatusCode);
                }

                return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            },
            TransientHttpFailure.IsTransient,
            cancellationToken).ConfigureAwait(false);

        return Deserialize(payload, typeInfo, endpoint);
    }

    private Task<string> GetStringAsync(string endpoint, CancellationToken cancellationToken)
        => _retryPolicy.ExecuteAsync(
            async (_, token) =>
            {
                using var message = CreateRequest(HttpMethod.Get, new Uri(ApiBaseUrl, endpoint));
                using var response = await _httpClient.SendAsync(message, token).ConfigureAwait(false);

                // Un vrai code d'erreur HTTP sur v1 ne vient jamais de l'application (elle répond
                // toujours 200) : c'est l'infrastructure qui parle, donc une panne réseau.
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            },
            TransientHttpFailure.IsTransient,
            cancellationToken);

    private static T Deserialize<T>(string payload, JsonTypeInfo<T> typeInfo, string endpoint)
    {
        try
        {
            return JsonSerializer.Deserialize(payload, typeInfo)
                ?? throw new JsonException($"Corps vide sur {endpoint}.");
        }
        catch (JsonException exception)
        {
            throw ModDbUnavailableException.FromNetworkFailure(exception);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri url)
    {
        var message = new HttpRequestMessage(method, url);
        message.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

        return message;
    }

    /// <summary>
    /// Vrai pour tout ce que les méthodes publiques de ce client doivent traduire en
    /// <see cref="ModDbUnavailableException"/> plutôt que laisser fuir tel quel : pannes de
    /// transport (<see cref="HttpRequestException"/>, <see cref="IOException"/>), payload illisible
    /// (<see cref="JsonException"/>), et délai dépassé. Ce dernier cas est le piège : le
    /// <see cref="HttpClient"/> lève la même <see cref="TaskCanceledException"/> qu'une annulation
    /// demandée par l'appelant. La seule façon fiable de les distinguer à cet endroit est de
    /// vérifier l'état du <see cref="CancellationToken"/> reçu par la méthode publique
    /// (<paramref name="callerToken"/>) : s'il est déclenché, c'est l'appelant qui a annulé — une
    /// décision, pas une panne — et l'exception doit ressortir intacte, jamais traduite. S'il ne
    /// l'est pas, la TaskCanceledException ne peut venir que d'un délai interne au HttpClient
    /// (<c>HttpClient.Timeout</c>) : c'est une panne réseau comme une autre.
    /// </summary>
    private static bool IsNetworkFailure(Exception exception, CancellationToken callerToken)
        => exception switch
        {
            OperationCanceledException => !callerToken.IsCancellationRequested,
            HttpRequestException or IOException or TimeoutException or JsonException or ModDbUnavailableException => true,
            _ => false,
        };

    private bool IsFresh(DateTimeOffset retrievedUtc)
    {
        var age = _clock.UtcNow - retrievedUtc;

        return age >= TimeSpan.Zero && age < _timeToLive;
    }

    private async Task<ModDbCacheDocument?> ReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var document = await _jsonFileStore
                .ReadAsync(CacheFilePath, ModDbJsonContext.Default.ModDbCacheDocument, cancellationToken)
                .ConfigureAwait(false);

            return document?.SchemaVersion == ModDbCacheDocument.CurrentSchemaVersion ? document : null;
        }
        catch (CorruptedFileException)
        {
            return null;
        }
    }

    private async Task WriteCacheAsync(ModDbCacheDocument document, CancellationToken cancellationToken)
    {
        try
        {
            await _jsonFileStore
                .WriteAsync(CacheFilePath, document, ModDbJsonContext.Default.ModDbCacheDocument, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cache non écrit (disque plein, dossier en lecture seule) : la réponse réseau reste
            // parfaitement valide, ce serait absurde de la jeter pour autant.
        }
    }
}