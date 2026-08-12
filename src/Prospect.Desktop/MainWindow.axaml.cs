using Avalonia.Controls;
using Avalonia.Platform;

using Prospect.Desktop.ViewModels.Shell;

namespace Prospect.Desktop;

public partial class MainWindow : Window
{
    /// <summary>
    /// Hauteur de notre barre de titre, en points. Doit rester égale au jeton <c>TitlebarH</c> de
    /// Styles/Tokens/Spacing.axaml, qui dimensionne la <c>TitlebarView</c> : un test le vérifie
    /// (ShellHeadlessTests), parce qu'une divergence entre les deux ne se verrait qu'à
    /// l'exécution, sur la seule plateforme où la zone de légende native compte.
    /// </summary>
    public const double CustomTitleBarHeight = 38;

    public MainWindow(ShellViewModel shellViewModel)
    {
        ArgumentNullException.ThrowIfNull(shellViewModel);

        InitializeComponent();
        DataContext = shellViewModel;

        // Unique point de bascule vers les décorations natives (docs/architecture.md), déjà
        // résolu par ShellViewModel à partir de l'OS courant (voir sa propriété UseCustomTitlebar) :
        // cette fenêtre se contente d'appliquer les propriétés de décoration qui en découlent.
        if (shellViewModel.UseCustomTitlebar)
        {
            // Les trois propriétés vont ensemble, et c'est l'oubli de la troisième qui donnait DEUX
            // barres de titre superposées sur Windows.
            //
            // ExtendClientAreaChromeHints vaut Default à la construction, et Default est un alias de
            // PreferSystemChrome (vérifié dans les métadonnées d'Avalonia 11.3.20 : la propriété est
            // enregistrée avec la valeur 2, et ExtendClientAreaChromeHints.PreferSystemChrome vaut
            // 2). Autrement dit, étendre la zone client sans rien dire du chrome revient à demander
            // au système de continuer à dessiner le sien PAR-DESSUS notre TitlebarView. NoChrome est
            // la seule valeur qui dit « je dessine la barre de titre moi-même ».
            //
            // SystemDecorations repasse à Full : BorderOnly retire le cadre non client sur lequel
            // s'appuient l'ombre, les poignées de redimensionnement et l'accrochage de fenêtre de
            // Windows. Ce n'est pas lui qui masque le chrome (c'est le rôle du hint ci-dessus), il
            // ne faisait que nous priver de ces comportements natifs.
            //
            // Enfin, la hauteur de légende est alignée sur notre propre barre plutôt que laissée à
            // -1 (« hauteur par défaut de la plateforme ») : c'est cette bande que le système
            // considère comme la zone de légende pour le déplacement et l'accrochage, et elle doit
            // donc coïncider avec ce que l'utilisateur voit.
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = CustomTitleBarHeight;
            SystemDecorations = SystemDecorations.Full;
        }
        else
        {
            ExtendClientAreaToDecorationsHint = false;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.Default;
            ExtendClientAreaTitleBarHeightHint = -1;
            SystemDecorations = SystemDecorations.Full;
        }
    }
}