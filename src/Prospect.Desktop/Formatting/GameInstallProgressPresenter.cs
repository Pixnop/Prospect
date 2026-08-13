using Prospect.Core.GameVersions;
using Prospect.Desktop.Resources;

namespace Prospect.Desktop.Formatting;

/// <summary>
/// Traduit un <see cref="GameInstallProgress"/> en la ligne de détail affichée sous la barre.
/// </summary>
/// <remarks>
/// Écrit une fois plutôt que trois : l'écran Versions, le wizard et l'import de modpack consomment
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

            // Rien à dire tant que la stratégie ne sait pas se mesurer : l'installeur Inno
            // silencieux ne publie aucun avancement, et la barre indéterminée porte alors seule le
            // message « ça travaille ».
            GameInstallPhase.Installing when progress.Ratio is { } ratio => UiText.Versions.InstallDetail((int)(ratio * 100d)),

            _ => string.Empty,
        };
    }
}