using CommunityToolkit.Mvvm.Input;

namespace Prospect.Desktop.ViewModels.Home;

/// <summary>
/// Marqueur de la tuile « nouvelle instance » en pointillés, dernier élément de la grille
/// (design/ui_kits/launcher/screen-home.jsx). Vit dans <see cref="HomeViewModel.GridItems"/> aux
/// côtés des <see cref="InstanceCardViewModel"/> plutôt qu'à part : un seul <c>ItemsControl</c> à
/// panneau <c>WrapPanel</c> les fait alors flotter ensemble jusqu'au bout de la grille, ce
/// qu'aucune combinaison de panneaux séparés ne permet d'obtenir.
/// </summary>
public sealed class NewInstanceTileViewModel(Action select)
{
    public IRelayCommand SelectCommand { get; } = new RelayCommand(select);
}