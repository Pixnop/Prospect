using Prospect.Core.GameVersions;
using Prospect.Core.Storage;
using Prospect.Desktop.Resources;

namespace Prospect.Desktop.Formatting;

/// <summary>
/// Traduit un <see cref="GameInstallProgress"/> en la ligne de détail affichée sous la barre.
/// </summary>
/// <remarks>
/// Écrit une fois plutôt que trois : l'écran Versions, le wizard et l'adoption VS Launcher consomment
/// le MÊME avancement et le rendaient chacun de leur côté, tous avec la même ligne
/// « si téléchargement, le détail, sinon rien ». Ce « sinon rien » est précisément ce qui laissait
/// la phase d'installation muette — une barre sans chiffre ni détail pendant l'extraction de
/// plusieurs centaines de mégaoctets, que rien ne distinguait d'un blocage. Le corriger à trois
/// endroits aurait garanti qu'un quatrième consommateur reparte du mauvais pied.
/// </remarks>
internal static class GameInstallProgressPresenter
{
    /// <summary>Ligne de détail : compteurs du téléchargement, avancement de l'extraction, ou rien.</summary>
    public static string DetailText(GameInstallProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return progress.Phase switch
        {
            GameInstallPhase.Downloading => UiText.Versions.DownloadDetail(
                ByteSizeFormatter.FormatProgress(progress.ReceivedBytes, progress.TotalBytes),
                ByteSizeFormatter.FormatSpeed(progress.BytesPerSecond)),

            // Une estimation le DIT. L'extraction d'un tar.gz connaît sa position exacte dans
            // l'archive ; l'installeur Windows, lui, ne publie rien et son avancement se déduit de
            // la croissance du dossier cible. Un tilde suffit à faire la différence entre les deux,
            // et il n'y a aucune raison de la cacher.
            GameInstallPhase.Installing when progress.Ratio is { } ratio => progress.IsEstimated
                ? UiText.Versions.InstallEstimateDetail((int)(ratio * 100d))
                : UiText.Versions.InstallDetail((int)(ratio * 100d)),

            // Rien à dire tant que la stratégie ne sait rien publier du tout : la barre
            // indéterminée porte alors seule le message « ça travaille ».
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Vrai quand il faut prévenir l'utilisateur AVANT que l'installeur du jeu n'ouvre sa propre
    /// fenêtre, c'est-à-dire uniquement quand cet installeur va réellement tourner.
    /// </summary>
    /// <remarks>
    /// Sous Windows, la voie normale est désormais l'EXTRACTION du contenu de l'installeur, qui
    /// n'exécute aucun script et n'ouvre donc aucune fenêtre : afficher la notice là serait annoncer
    /// une boîte qui ne viendra pas. Elle n'accompagne plus que le repli, signalé par
    /// <see cref="GameInstallProgress.RunsVendorInstaller"/>. Un avertissement que l'on voit à
    /// chaque installation sans qu'il ne se passe rien est un avertissement que l'on cesse de lire,
    /// et celui-ci porte une question dont la réponse par défaut désinstalle le jeu.
    /// </remarks>
    public static bool ShowsInstallerPromptNotice(GameInstallProgress progress, AppOperatingSystem operatingSystem)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return operatingSystem == AppOperatingSystem.Windows
            && progress.Phase == GameInstallPhase.Installing
            && progress.RunsVendorInstaller;
    }
}