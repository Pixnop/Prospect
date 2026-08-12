using System.IO.Abstractions;

using Prospect.Core.Common;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;

namespace Prospect.Tests.Live;

/// <summary>
/// Monte un <see cref="ModDbClient"/> de production contre le VRAI ModDB. Aucun double : c'est le
/// client réel, le mapper réel et le cache disque réel, seulement pointés vers un dossier
/// temporaire jeté à la fin du test pour ne pas écraser le cache de l'utilisateur.
/// </summary>
/// <remarks>
/// Deux garde-fous de politesse, exigés par docs/research/moddb-api.md (aucun rate limiting
/// applicatif n'a été trouvé côté serveur, ce qui est une raison d'être prudent, pas l'inverse) :
/// les requêtes sortantes sont espacées d'au moins <see cref="MinimumInterval"/> par
/// <see cref="SpacedRequestHandler"/>, et le <c>User-Agent</c> du client (déjà
/// <c>Prospect/&lt;version&gt;</c>) est suffixé pour que le trafic de tests soit distinguable de
/// celui d'un vrai utilisateur.
/// </remarks>
public sealed class LiveModDb : IDisposable
{
    /// <summary>Délai minimal entre deux requêtes sortantes de l'étage live.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1.5);

    /// <summary><c>User-Agent</c> de l'étage live : celui du client, suffixé pour être distinguable côté serveur.</summary>
    public static string LiveUserAgent { get; } = $"{ModDbClient.UserAgent} (live-tests)";

    private readonly string _root;
    private readonly HttpClient _httpClient;
    private readonly ModDbClient _client;

    /// <summary>Construit le client live et son cache disque temporaire.</summary>
    public LiveModDb()
    {
        _root = Path.Combine(Path.GetTempPath(), "prospect-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _httpClient = new HttpClient(new SpacedRequestHandler())
        {
            // Le catalogue complet pèse 3,5 Mo : le délai par défaut de 100 s suffit largement,
            // mais l'écrire ici évite qu'un jour de mauvaise liaison ressemble à une régression.
            Timeout = TimeSpan.FromMinutes(2),
        };

        var fileSystem = new FileSystem();
        _client = new ModDbClient(
            _httpClient,
            new JsonFileStore(fileSystem),
            new AppPaths(new SystemAppEnvironment(), _root),
            new SystemClock());
    }

    /// <summary>Le client de production, à interroger tel quel.</summary>
    public IModDbClient Client => _client;

    /// <summary>Ferme le client et supprime son cache temporaire.</summary>
    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Un cache temporaire non supprimé n'invalide aucun test : l'OS le nettoiera.
        }
    }

    /// <summary>
    /// Sérialise les requêtes de l'étage live et impose un délai entre deux départs. Écrit comme
    /// un <see cref="DelegatingHandler"/> plutôt que comme des <c>Task.Delay</c> dans les tests :
    /// le comptage ne peut pas être oublié par un test qui ajoute un appel.
    /// </summary>
    private sealed class SpacedRequestHandler : DelegatingHandler
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static DateTimeOffset _lastRequestUtc = DateTimeOffset.MinValue;

        public SpacedRequestHandler()
            : base(new HttpClientHandler())
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            // Suffixe de courtoisie : le ModDB voit « Prospect/0.1.0-dev (live-tests) » et peut
            // distinguer nos tests d'un vrai launcher sans que le client change de comportement.
            request.Headers.Remove("User-Agent");
            request.Headers.TryAddWithoutValidation("User-Agent", LiveUserAgent);

            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var since = DateTimeOffset.UtcNow - _lastRequestUtc;
                if (since < MinimumInterval)
                {
                    await Task.Delay(MinimumInterval - since, cancellationToken).ConfigureAwait(false);
                }

                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lastRequestUtc = DateTimeOffset.UtcNow;
                Gate.Release();
            }
        }
    }
}