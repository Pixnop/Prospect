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
public sealed record GameInstallProgress(
    GameInstallPhase Phase,
    double? Ratio,
    long ReceivedBytes,
    long? TotalBytes,
    double BytesPerSecond)
{
    /// <summary>Passage à une étape dont l'avancement n'est pas chiffrable (extraction, installeur).</summary>
    public static GameInstallProgress ForPhase(GameInstallPhase phase) => new(phase, null, 0L, null, 0d);

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