using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Prospect.Core.Tests.Http;

/// <summary>
/// Serveur de fichiers factice : répond aux <c>HEAD</c> avec un <c>Content-Length</c>, aux
/// <c>GET</c> avec le contenu complet, et honore l'en-tête <c>Range</c> comme le font les deux
/// miroirs officiels. Son intérêt est de pouvoir mentir sur commande : couper en pleine
/// réception, ignorer <c>Range</c>, refuser la plage demandée, tomber en panne.
/// </summary>
[SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "Calcule l'empreinte attendue par les tests, en miroir du condensat que publie le catalogue officiel.")]
internal sealed class FakeDownloadServer
{
    private int _getCount;

    public FakeDownloadServer(byte[] content)
    {
        Content = content;
        Md5 = Convert.ToHexStringLower(MD5.HashData(content));
    }

    /// <summary>Contenu servi.</summary>
    public byte[] Content { get; }

    /// <summary>Empreinte MD5 réelle du contenu, telle que le catalogue l'annoncerait.</summary>
    public string Md5 { get; }

    /// <summary>Nombre d'octets à livrer avant de couper, par numéro de <c>GET</c> (index 0 = premier GET). <see langword="null"/> = réponse complète.</summary>
    public IReadOnlyList<int?> FaultPlan { get; set; } = [];

    /// <summary>Faux : le serveur ignore <c>Range</c> et renvoie systématiquement le fichier entier en 200.</summary>
    public bool SupportsRange { get; set; } = true;

    /// <summary>Faux : le serveur ne répond pas aux <c>HEAD</c>.</summary>
    public bool SupportsHead { get; set; } = true;

    /// <summary>Vrai : le serveur répond 416 à toute demande de plage.</summary>
    public bool RejectRange { get; set; }

    /// <summary>Rappel invoqué après chaque bloc livré, avec le nombre d'octets déjà envoyés dans la réponse courante.</summary>
    public Action<int>? AfterChunk { get; set; }

    /// <summary>Taille des blocs livrés.</summary>
    public int ChunkSize { get; set; } = 64;

    /// <summary>Nombre de <c>GET</c> reçus.</summary>
    public int GetCount => _getCount;

    public HttpResponseMessage Handle(HttpRequestMessage request)
    {
        if (request.Method == HttpMethod.Head)
        {
            return HandleHead();
        }

        var index = _getCount++;
        var from = SupportsRange ? (int?)request.Headers.Range?.Ranges.FirstOrDefault()?.From : null;

        if (RejectRange && request.Headers.Range is not null)
        {
            return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
        }

        var offset = from ?? 0;
        var payload = Content.AsSpan(offset).ToArray();
        var faultAfter = index < FaultPlan.Count ? FaultPlan[index] : null;
        var stream = new ChunkedStream(payload, ChunkSize, faultAfter, AfterChunk);

        var response = new HttpResponseMessage(offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        };
        response.Content.Headers.ContentLength = payload.Length;
        if (offset > 0)
        {
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, Content.Length - 1, Content.Length);
        }

        return response;
    }

    private HttpResponseMessage HandleHead()
    {
        if (!SupportsHead)
        {
            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        response.Content.Headers.ContentLength = Content.Length;
        response.Headers.AcceptRanges.Add("bytes");

        return response;
    }
}