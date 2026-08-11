namespace Prospect.Core.Http;

/// <summary>
/// Avancement d'un téléchargement à un instant donné.
/// </summary>
/// <param name="ReceivedBytes">Octets déjà écrits sur disque, reprise comprise.</param>
/// <param name="TotalBytes">
/// Taille totale annoncée par le serveur (<c>Content-Length</c>), ou <see langword="null"/> quand
/// il ne l'annonce pas. Jamais dérivée de la chaîne <c>filesize</c> du catalogue, qui est
/// approximative et destinée à l'affichage.
/// </param>
/// <param name="BytesPerSecond">Vitesse instantanée lissée.</param>
/// <param name="State">
/// État de l'opération au moment de la mesure. Il voyage avec l'avancement pour que l'appelant
/// distingue « je reçois des octets » de « je vérifie l'empreinte » sans avoir à surveiller la
/// file en parallèle : c'est ce qui permet à l'installation d'annoncer ses phases.
/// </param>
public sealed record DownloadProgress(long ReceivedBytes, long? TotalBytes, double BytesPerSecond, DownloadState State = DownloadState.Running)
{
    /// <summary>État de départ, avant le premier octet.</summary>
    public static DownloadProgress None { get; } = new(0, null, 0d, DownloadState.Queued);

    /// <summary>
    /// Avancement entre 0 et 1, ou <see langword="null"/> quand le total est inconnu : la barre de
    /// progression passe alors en mode indéterminé plutôt que d'afficher un pourcentage inventé.
    /// </summary>
    public double? Ratio => TotalBytes is > 0 ? Math.Clamp((double)ReceivedBytes / TotalBytes.Value, 0d, 1d) : null;
}