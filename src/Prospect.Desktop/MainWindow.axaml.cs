using Avalonia.Controls;

using Prospect.Desktop.ViewModels.Shell;

namespace Prospect.Desktop;

public partial class MainWindow : Window
{
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
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = -1;
            SystemDecorations = SystemDecorations.BorderOnly;
        }
        else
        {
            ExtendClientAreaToDecorationsHint = false;
            SystemDecorations = SystemDecorations.Full;
        }
    }
}