using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Prospect.Desktop.ViewModels.Shell;

/// <summary>
/// Une entrée de la sidebar (design/ui_kits/launcher/app-shell.jsx, <c>NavItem</c>). Porte la
/// page qu'elle active (<see cref="Page"/>) plutôt qu'une simple étiquette : <see cref="ShellViewModel"/>
/// s'en sert pour retrouver quelle entrée marquer active après une navigation, sans référence
/// circulaire entre l'item et lui-même au moment de la construction.
/// </summary>
public sealed partial class NavItemViewModel : ObservableObject
{
    private readonly Action<object> _select;

    public NavItemViewModel(string iconKey, string label, object page, Action<object> select, int? count = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(select);

        IconKey = iconKey;
        Label = label;
        Page = page;
        Count = count;
        _select = select;
    }

    /// <summary>Clé résolue par <see cref="Prospect.Desktop.Converters.IconKeyToGeometryConverter"/>.</summary>
    public string IconKey { get; }

    public string Label { get; }

    /// <summary>ViewModel de la page que cette entrée active.</summary>
    public object Page { get; }

    /// <summary>Compteur affiché en fin de ligne, <see langword="null"/> pour ne rien afficher.</summary>
    public int? Count { get; }

    [ObservableProperty]
    private bool _isActive;

    [RelayCommand]
    private void Select() => _select(Page);
}