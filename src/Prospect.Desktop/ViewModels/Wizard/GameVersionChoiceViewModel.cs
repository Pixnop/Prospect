using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Desktop.Formatting;

namespace Prospect.Desktop.ViewModels.Wizard;

/// <summary>
/// Une version proposée à l'étape 2 du wizard (design/ui_kits/launcher/screen-wizard.jsx) :
/// numéro, badge de canal, et à droite « installée » ou la taille à télécharger.
/// </summary>
public sealed partial class GameVersionChoiceViewModel : ObservableObject
{
    private readonly Action<GameVersionChoiceViewModel> _select;

    public GameVersionChoiceViewModel(GameVersion version, bool isInstalled, string stateText, Action<GameVersionChoiceViewModel> select)
    {
        ArgumentNullException.ThrowIfNull(select);

        Version = version;
        VersionText = version.ToString();
        ChannelBadgeTone = ChannelBadgePresentation.ToBadgeTone(version.Channel);
        IsInstalled = isInstalled;
        StateText = stateText;
        _select = select;
    }

    public GameVersion Version { get; }

    public string VersionText { get; }

    /// <summary>Teinte de badge du design (<c>stable</c>, <c>unstable</c>, <c>pre</c>).</summary>
    public string ChannelBadgeTone { get; }

    /// <summary>Vrai pour les versions déjà présentes sur le disque, listées en premier.</summary>
    public bool IsInstalled { get; }

    /// <summary>« installée » ou « 590.5 MB à télécharger ».</summary>
    public string StateText { get; }

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => _select(this);
}