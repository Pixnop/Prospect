using Avalonia.Controls;

using Prospect.Desktop.ViewModels.Mods;

namespace Prospect.Desktop.Views.Mods;

/// <summary>
/// Deuxième dérogation documentée du projet à la règle « zéro code-behind au-delà
/// d'InitializeComponent », après TitlebarView : l'extension automatique de la fenêtre de rendu à
/// l'approche du bas du défilement. La position et l'étendue d'un
/// <see cref="Avalonia.Controls.ScrollViewer"/> sont un fait de VUE, pas un état applicatif ; les
/// exposer à un ViewModel supposerait de lui faire connaître des pixels. La décision, elle, reste
/// dans le ViewModel : ce code-behind ne fait qu'invoquer <c>ShowMoreCommand</c>, qui décide seul
/// s'il reste quelque chose à montrer.
/// </summary>
public partial class ModBrowserView : UserControl
{
    /// <summary>
    /// Distance au bas du contenu, en pixels, sous laquelle la tranche suivante est demandée. Un
    /// peu plus qu'une hauteur de carte (204 px) : la tranche arrive avant que l'utilisateur ne
    /// touche le fond, plutôt qu'après.
    /// </summary>
    private const double PrefetchDistance = 320;

    public ModBrowserView() => InitializeComponent();

    private void OnResultsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll || DataContext is not ModBrowserViewModel browser)
        {
            return;
        }

        var remaining = scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y;
        if (remaining <= PrefetchDistance && browser.ShowMoreCommand.CanExecute(null))
        {
            browser.ShowMoreCommand.Execute(null);
        }
    }
}