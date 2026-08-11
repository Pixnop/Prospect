using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Prospect.Desktop.Views.Shell;

/// <summary>
/// Voir le commentaire d'en-tête de TitlebarView.axaml : seule dérogation de ce projet à la règle
/// MVVM stricte « zéro code-behind au-delà d'InitializeComponent ». Chaque méthode ci-dessous ne
/// fait que transférer un évènement de vue vers l'API native de <see cref="Window"/>
/// (<see cref="Window.BeginMoveDrag"/>, <see cref="Window.WindowState"/>, <see cref="Window.Close()"/>) :
/// aucune décision métier, rien qu'un ViewModel testable pourrait porter à la place.
/// </summary>
public partial class TitlebarView : UserControl
{
    public TitlebarView() => InitializeComponent();

    private void OnDragRegionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (VisualRoot is not Window window)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize(window);
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            window.BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is Window window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnToggleMaximizeClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is Window window)
        {
            ToggleMaximize(window);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => (VisualRoot as Window)?.Close();

    // Met à jour l'icône réduire/agrandir localement, plutôt que de s'abonner à WindowStateProperty :
    // les seuls chemins qui changent WindowState dans cette vue sont ceux-ci (bouton, double-clic),
    // donc une mise à jour synchrone après coup suffit sans introduire d'abonnement à nettoyer.
    private void ToggleMaximize(Window window)
    {
        var wasMaximized = window.WindowState == WindowState.Maximized;
        window.WindowState = wasMaximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeIcon.IsVisible = wasMaximized;
        RestoreIcon.IsVisible = !wasMaximized;
    }
}