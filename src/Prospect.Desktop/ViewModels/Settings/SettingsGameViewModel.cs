using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.Storage;

namespace Prospect.Desktop.ViewModels.Settings;

/// <summary>
/// Section Jeu de l'écran Réglages : emplacement des données de Prospect (racine sous laquelle
/// vivent instances, versions du jeu et cache, voir docs/architecture.md « Arborescence disque »)
/// et bouton pour l'ouvrir dans le gestionnaire de fichiers du système. La relocalisation de cette
/// racine (déplacer <c>~/.local/share/prospect</c> ailleurs) est hors périmètre : cette page se
/// contente de l'afficher honnêtement, sans laisser croire qu'un bouton la changerait.
/// </summary>
public sealed partial class SettingsGameViewModel : ObservableObject
{
    private readonly IExternalUrlOpener _urlOpener;

    public SettingsGameViewModel(AppPaths appPaths, IExternalUrlOpener urlOpener)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(urlOpener);

        _urlOpener = urlOpener;
        DataPath = appPaths.RootDirectory;
    }

    /// <summary>Racine de toutes les données de Prospect, affichée en mono dans la vue.</summary>
    public string DataPath { get; }

    [RelayCommand]
    private Task<bool> OpenDataFolderAsync() => _urlOpener.OpenFolderAsync(DataPath);
}