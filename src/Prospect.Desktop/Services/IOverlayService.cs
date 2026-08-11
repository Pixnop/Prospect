namespace Prospect.Desktop.Services;

/// <summary>
/// Emplacement unique pour le panneau modal affiché par-dessus le shell : wizard de création ou
/// dialogue de carte (renommer/dupliquer/supprimer). Un seul peut être actif à la fois, ce qui
/// correspond exactement à l'usage du produit (design/ui_kits/launcher/screen-wizard.jsx, un
/// panneau avec voile par-dessus toute la fenêtre). La vue résout <see cref="Active"/> vers son
/// contrôle via le <see cref="Prospect.Desktop.ViewLocator"/> global, donc ce service ne connaît
/// lui-même aucun type de vue.
/// </summary>
public interface IOverlayService
{
    /// <summary>ViewModel du panneau actuellement affiché, ou <see langword="null"/> si aucun.</summary>
    object? Active { get; }

    /// <summary>Affiche <paramref name="overlayViewModel"/>, remplaçant le panneau actif s'il y en a un.</summary>
    void Show(object overlayViewModel);

    /// <summary>Referme le panneau actif. Sans effet si aucun n'est affiché.</summary>
    void Close();
}