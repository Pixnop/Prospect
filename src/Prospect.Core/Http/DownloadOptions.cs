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
/// <param name="ProgressInterval">
/// Délai minimal entre deux notifications d'avancement. La cadence est une durée et non une
/// quantité d'octets, contrairement à ce que ce réglage a longtemps été : un pas en octets publie
/// des centaines de fois par seconde sur une bonne ligne et une fois toutes les dix secondes sur
/// une mauvaise, c'est-à-dire au rythme de la grandeur qu'on cherche justement à afficher
/// posément. <see cref="TimeSpan.Zero"/> publie à chaque bloc lu, ce dont seuls les tests ont
/// l'usage.
/// </param>
/// <param name="HistoryLimit">
/// Nombre d'opérations TERMINÉES gardées dans la file. Au-delà, les plus anciennes sortent seules.
/// L'historique vit le temps de la session et rien n'est écrit sur disque : un historique persisté
/// serait une autre décision (quoi garder, combien de temps, comment le purger), pas une simple
/// extension de celle-ci.
/// </param>
public sealed record DownloadOptions(
    int MaxParallelDownloads,
    TimeSpan ReadInactivityTimeout,
    int BufferSize,
    TimeSpan ProgressInterval,
    int HistoryLimit = 20)
{
    /// <summary>
    /// Réglage par défaut : deux téléchargements en parallèle, 90 s d'inactivité tolérées, blocs de
    /// 128 Ko, quatre avancements par seconde.
    /// </summary>
    public static DownloadOptions Default { get; } = new(2, TimeSpan.FromSeconds(90), 128 * 1024, TimeSpan.FromMilliseconds(250));
}