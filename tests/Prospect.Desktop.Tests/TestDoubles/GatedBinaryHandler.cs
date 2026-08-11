using System.Net;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Gestionnaire HTTP factice servant un petit contenu binaire, avec une barrière optionnelle sur
/// le <c>GET</c> : tant que le test ne l'ouvre pas, le téléchargement reste en cours, ce qui rend
/// observable un état transitoire de la file sans dépendre du minutage.
/// </summary>
internal sealed class GatedBinaryHandler : HttpMessageHandler
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public GatedBinaryHandler(byte[] content, bool gated = false, Exception? failure = null)
    {
        Content = content;
        IsGated = gated;
        Failure = failure;
    }

    public byte[] Content { get; }

    public bool IsGated { get; }

    public Exception? Failure { get; }

    /// <summary>Signalée dès que le premier <c>GET</c> est arrivé, barrière comprise.</summary>
    public TaskCompletionSource GetReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Laisse passer le <c>GET</c> retenu.</summary>
    public void Release() => _gate.TrySetResult();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        if (request.Method == HttpMethod.Head)
        {
            var head = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            head.Content.Headers.ContentLength = Content.Length;

            return head;
        }

        GetReceived.TrySetResult();
        if (IsGated)
        {
            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Content) };
        response.Content.Headers.ContentLength = Content.Length;

        return response;
    }
}