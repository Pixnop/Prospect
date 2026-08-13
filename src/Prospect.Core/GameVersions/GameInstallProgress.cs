using Prospect.Core.Http;

namespace Prospect.Core.GameVersions;

/// <summary>Étape courante d'une installation de version du jeu.</summary>
public enum GameInstallPhase
{
    /// <summary>Réception du fichier depuis un miroir.</summary>
    Downloading,

    /// <summary>Contrôle de l'empreinte MD5 du fichier reçu.</summary>
    Verifying,

    /// <summary>Extraction de l'archive, ou exécution de l'installeur Windows.</summary>
    Installing,

    /// <summary>Installation terminée, fichier sentinelle écrit.</summary>
    Completed,
}

/// <summary>
/// Avancement agrégé d'une installation : la phase, plus les compteurs du téléchargement quand
/// c'est lui qui progresse.
/// </summary>
/// <param name="Phase">Étape courante.</param>
/// <param name="Ratio">Avancement entre 0 et 1, ou <see langword="null"/> pour une étape dont la durée n'est pas mesurable.</param>
/// <param name="ReceivedBytes">Octets reçus, pendant la phase de téléchargement.</param>
/// <param name="TotalBytes">Taille totale annoncée par le serveur, si connue.</param>
/// <param name="BytesPerSecond">Vitesse lissée, pendant la phase de téléchargement.</param>
/// <param name="IsEstimated">
/// Vrai quand le ratio est ESTIMÉ et non mesuré. L'extraction d'un <c>.tar.gz</c> connaît sa
/// position exacte dans l'archive ; l'installeur Windows silencieux ne publie rien, et son
/// avancement est déduit de la croissance du dossier cible. L'interface le dit alors au lieu de
/// laisser croire à un décompte exact.
/// </param>
public sealed record GameInstallProgress(
    GameInstallPhase Phase,
    double? Ratio,
    long ReceivedBytes,
    long? TotalBytes,
    double BytesPerSecond,
    bool IsEstimated = false)
{
    /// <summary>Passage à une étape dont l'avancement n'est pas chiffrable (installeur silencieux).</summary>
    public static GameInstallProgress ForPhase(GameInstallPhase phase) => new(phase, null, 0L, null, 0d);

    /// <summary>
    /// Avancement CHIFFRÉ de la phase d'installation, mesuré (extraction d'un <c>.tar.gz</c>) ou
    /// estimé (croissance du dossier cible sous l'installeur Windows). Les compteurs d'octets
    /// restent à zéro : ce sont ceux du téléchargement, et les reprendre ici pour publier une autre
    /// grandeur les rendrait mensongers.
    /// </summary>
    /// <param name="ratio">Avancement entre 0 et 1.</param>
    /// <param name="isEstimated">Vrai pour une estimation, qui sera étiquetée comme telle.</param>
    public static GameInstallProgress ForInstalling(double ratio, bool isEstimated = false)
        => new(GameInstallPhase.Installing, Math.Clamp(ratio, 0d, 1d), 0L, null, 0d, isEstimated);

    /// <summary>Traduit un avancement de téléchargement en avancement d'installation.</summary>
    public static GameInstallProgress FromDownload(DownloadProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return new GameInstallProgress(
            MapPhase(progress.State),
            progress.Ratio,
            progress.ReceivedBytes,
            progress.TotalBytes,
            progress.BytesPerSecond);
    }

    private static GameInstallPhase MapPhase(DownloadState state) => state switch
    {
        DownloadState.Verifying => GameInstallPhase.Verifying,
        _ => GameInstallPhase.Downloading,
    };
}