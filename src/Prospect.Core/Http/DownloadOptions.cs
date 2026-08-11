namespace Prospect.Core.Http;

/// <summary>
/// Réglages du moteur de téléchargement.
/// </summary>
/// <param name="MaxParallelDownloads">Téléchargements simultanés. Au-delà, les demandes attendent dans l'état <see cref="DownloadState.Queued"/>.</param>
/// <param name="ReadInactivityTimeout">
/// Délai maximal sans le moindre octet reçu. C'est volontairement un délai d'inactivité et non un
/// délai total : un client de 600 Mo peut légitimement mettre une heure sur une ligne lente, mais
/// jamais rester deux minutes sans rien recevoir.
/// </param>
/// <param name="BufferSize">Taille des blocs de lecture et de calcul d'empreinte.</param>
/// <param name="ProgressStepBytes">Quantité d'octets entre deux notifications d'avancement, pour ne pas noyer l'interface.</param>
public sealed record DownloadOptions(
    int MaxParallelDownloads,
    TimeSpan ReadInactivityTimeout,
    int BufferSize,
    long ProgressStepBytes)
{
    /// <summary>Réglage par défaut : deux téléchargements en parallèle, 90 s d'inactivité tolérées, blocs de 128 Ko.</summary>
    public static DownloadOptions Default { get; } = new(2, TimeSpan.FromSeconds(90), 128 * 1024, 512 * 1024);
}