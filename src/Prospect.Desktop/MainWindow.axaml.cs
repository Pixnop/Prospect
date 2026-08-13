using Avalonia.Controls;
using Avalonia.Input;

using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.Windowing;

namespace Prospect.Desktop;

public partial class MainWindow : Window
{
    /// <summary>
    /// Hauteur de notre barre de titre, en points. Doit rester égale au jeton <c>TitlebarH</c> de
    /// Styles/Tokens/Spacing.axaml, qui dimensionne la <c>TitlebarView</c> : un test le vérifie
    /// (ShellHeadlessTests), parce qu'une divergence entre les deux ne se verrait qu'à
    /// l'exécution, sur la seule plateforme où la zone de légende native compte.
    /// </summary>
    public const double CustomTitleBarHeight = WindowChromeSettings.CustomTitleBarHeight;

    public MainWindow(ShellViewModel shellViewModel)
    {
        ArgumentNullException.ThrowIfNull(shellViewModel);

        InitializeComponent();
        DataContext = shellViewModel;

        // Toute la décision de décoration vit dans WindowChromeSettings, résolue par le shell à
        // partir de l'OS courant : cette fenêtre ne fait que l'appliquer. C'est ce qui rend la
        // règle par système testable (WindowChromeSettingsTests), là où un enchaînement de « if »
        // ici ne serait vérifiable que sur trois machines.
        var chrome = shellViewModel.WindowChrome;
        ExtendClientAreaToDecorationsHint = chrome.ExtendClientAreaToDecorations;
        ExtendClientAreaChromeHints = chrome.ChromeHints;
        ExtendClientAreaTitleBarHeightHint = chrome.TitleBarHeightHint;
        SystemDecorations = chrome.SystemDecorations;
    }

    /// <summary>
    /// Démarre le redimensionnement depuis une poignée de bord. Le bord vient du <c>Tag</c> du
    /// contrôle pressé, posé en XAML : les huit zones se distinguent par leur position, pas par
    /// huit gestionnaires recopiés.
    /// </summary>
    /// <remarks>
    /// Même dérogation documentée que <c>TitlebarView</c> à la règle « zéro code-behind » :
    /// <see cref="Window.BeginResizeDrag"/> est une primitive native que rien ne rend liable à une
    /// commande, et cette méthode ne décide de rien qu'un ViewModel pourrait porter. Ces poignées
    /// n'existent que sous Linux, où <see cref="SystemDecorations.None"/> retire le cadre serveur
    /// qui les offrait (voir <see cref="WindowChromeSettings"/>).
    /// </remarks>
    private void OnResizeGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string edgeName }
            || !Enum.TryParse<WindowEdge>(edgeName, out var edge)
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Une fenêtre maximisée n'a pas de bord à tirer : la laisser démarrer un redimensionnement
        // la ferait sortir de son état maximisé d'un simple frôlement.
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        BeginResizeDrag(edge, e);
        e.Handled = true;
    }
}