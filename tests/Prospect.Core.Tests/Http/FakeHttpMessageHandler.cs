using System.Net;

namespace Prospect.Core.Tests.Http;

/// <summary>
/// Gestionnaire HTTP factice : tout le réseau des tests passe par ici, aucun appel réel n'est
/// jamais émis (ni en local, ni en CI). Le répondeur reçoit la requête et rend soit une réponse,
/// soit une exception à lever, ce qui permet de simuler pannes, 5xx, coupures en plein flux et
/// bascules de miroir.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    /// <summary>Requêtes reçues, dans l'ordre : méthode, URL et en-têtes restent inspectables après coup.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Nombre de requêtes reçues pour une URL donnée.</summary>
    public int CountFor(Uri url) => Requests.Count(request => request.Url == url);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Range?.ToString()));

        return Task.FromResult(_responder(request));
    }

    /// <summary>Réponse texte simple.</summary>
    public static HttpResponseMessage Text(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode) { Content = new StringContent(body) };

    /// <summary>Réponse d'erreur sans corps.</summary>
    public static HttpResponseMessage Status(HttpStatusCode statusCode) => new(statusCode);
}

/// <summary>Trace d'une requête reçue par <see cref="FakeHttpMessageHandler"/>.</summary>
/// <param name="Method">Verbe HTTP.</param>
/// <param name="Url">URL demandée.</param>
/// <param name="RangeHeader">En-tête <c>Range</c> tel qu'envoyé, ou <see langword="null"/>.</param>
internal sealed record RecordedRequest(HttpMethod Method, Uri Url, string? RangeHeader);