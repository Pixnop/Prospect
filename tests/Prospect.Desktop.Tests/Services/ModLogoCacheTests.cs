using System.Net;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;

using Shouldly;

namespace Prospect.Desktop.Tests.Services;

/// <summary>
/// <see cref="ModLogoCache"/> en isolation, réseau simulé par <see cref="RecordingHandler"/> :
/// miss (premier appel, décodage réel), hit (deuxième appel, aucune requête de plus), échec réseau
/// et contenu non décodable (repli sur <see langword="null"/>, jamais d'exception), et annulation
/// (celle de l'appelant seule doit ressortir, sans empoisonner le cache pour une future tentative).
/// Marqué <c>[AvaloniaFact]</c> partout : même les chemins d'échec construisent réellement un
/// <see cref="Avalonia.Media.Imaging.Bitmap"/> ou tentent de le faire, ce qui exige la plateforme
/// Avalonia (voir TestAppBuilder).
/// </summary>
public sealed class ModLogoCacheTests
{
    private static readonly Uri LogoUrl = new("https://moddbcdn.vintagestory.at/example.png");

    /// <summary>Gestionnaire HTTP factice piloté par une fonction, avec compteur d'appels.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            return _respond(request, cancellationToken);
        }
    }

    private static RecordingHandler CreateSuccessHandler(byte[] bytes)
        => new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));

    /// <summary>
    /// Une vignette aux dimensions du CDN (480 px de côté, ce que sert réellement
    /// <c>moddbcdn.vintagestory.at</c>) est ramenée à la taille d'affichage de la carte. Sans cette
    /// réduction, le catalogue réel — 5 433 logos — faisait monter le jeu de travail à près de
    /// 8 Gio, mesuré : c'est la cause du plantage rapporté en conditions réelles.
    /// </summary>
    [AvaloniaFact]
    public async Task GetAsync_CdnSizedLogo_IsShrunkToTheCardDisplaySize()
    {
        var handler = CreateSuccessHandler(TinyPng.Create(size: 480));
        using var cache = new ModLogoCache(new HttpClient(handler));

        var bitmap = await cache.GetAsync(LogoUrl);

        bitmap.ShouldNotBeNull();
        bitmap.PixelSize.Width.ShouldBe(ModLogoCache.MaxLogoWidth);
        bitmap.PixelSize.Height.ShouldBe(ModLogoCache.MaxLogoWidth);
    }

    /// <summary>
    /// Régression exacte du plantage : un <c>Image</c> de l'arbre visuel garde une référence vers
    /// le bitmap que le cache lui a rendu. Si le cache le libérait à sa propre fermeture — ce que
    /// fait <c>App</c> en libérant le conteneur sur <c>ShutdownRequested</c>, alors que la fenêtre
    /// est encore vivante — la passe de mise en page suivante levait une
    /// <see cref="NullReferenceException"/> dans <c>Avalonia.Controls.Image.MeasureOverride</c>.
    /// </summary>
    [AvaloniaFact]
    public async Task Dispose_LeavesTheBitmapsItHandedOutUsableByTheVisualTree()
    {
        var handler = CreateSuccessHandler(TinyPng.Create(size: 8));
        var cache = new ModLogoCache(new HttpClient(handler));
        var bitmap = await cache.GetAsync(LogoUrl);

        var window = new Window { Width = 200, Height = 200, Content = new Image { Source = bitmap } };
        window.Show();
        window.Settle();

        cache.Dispose();

        Should.NotThrow(() =>
        {
            window.InvalidateMeasure();
            window.Settle();
        });

        window.Close();
    }

    [AvaloniaFact]
    public async Task GetAsync_Miss_FetchesAndDecodesTheBitmap()
    {
        var handler = CreateSuccessHandler(TinyPng.Create(size: 3));
        using var cache = new ModLogoCache(new HttpClient(handler));

        var bitmap = await cache.GetAsync(LogoUrl);

        bitmap.ShouldNotBeNull();
        bitmap.PixelSize.Width.ShouldBe(3);
        bitmap.PixelSize.Height.ShouldBe(3);
        handler.RequestCount.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task GetAsync_Hit_ReturnsTheSameCachedBitmapWithoutANewRequest()
    {
        var handler = CreateSuccessHandler(TinyPng.Create());
        using var cache = new ModLogoCache(new HttpClient(handler));

        var first = await cache.GetAsync(LogoUrl);
        var second = await cache.GetAsync(LogoUrl);

        second.ShouldBeSameAs(first);
        handler.RequestCount.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task GetAsync_NetworkFailure_ReturnsNullInsteadOfThrowing()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("panne réseau simulée"));
        using var cache = new ModLogoCache(new HttpClient(handler));

        var result = await cache.GetAsync(LogoUrl);

        result.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task GetAsync_ServerErrorStatusCode_ReturnsNullInsteadOfThrowing()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var cache = new ModLogoCache(new HttpClient(handler));

        var result = await cache.GetAsync(LogoUrl);

        result.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task GetAsync_ContentIsNotAnImage_ReturnsNullInsteadOfThrowing()
    {
        var handler = CreateSuccessHandler([0x00, 0x01, 0x02, 0x03]);
        using var cache = new ModLogoCache(new HttpClient(handler));

        var result = await cache.GetAsync(LogoUrl);

        result.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task GetAsync_CallerCancels_ThrowsOperationCanceledExceptionAndLeavesTheUrlRetryable()
    {
        var gate = new TaskCompletionSource();
        var handler = new RecordingHandler(async (_, token) =>
        {
            // Ne répond jamais tant que le test ne le débloque pas explicitement : le jeton
            // d'annulation doit être ce qui interrompt l'attente, pas une réponse qui arrive à temps.
            await gate.Task.WaitAsync(token).ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TinyPng.Create()) };
        });
        using var cache = new ModLogoCache(new HttpClient(handler));
        using var cts = new CancellationTokenSource();

        var pending = cache.GetAsync(LogoUrl, cts.Token);
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => pending);

        // Une annulation volontaire n'est pas un échec à mémoriser : un appel ultérieur sans jeton
        // annulé doit pouvoir réessayer plutôt que de rester bloqué sur l'annulation précédente.
        gate.SetResult();
        var retry = await cache.GetAsync(LogoUrl);

        retry.ShouldNotBeNull();
        handler.RequestCount.ShouldBe(2);
    }
}