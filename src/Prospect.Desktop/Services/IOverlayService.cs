namespace Prospect.Desktop.Services;

/// <summary>
/// Emplacement unique pour le panneau modal affiché par-dessus le shell : wizard de création ou
/// dialogue de carte (renommer/dupliquer/supprimer). Un seul peut être actif à la fois, ce qui
/// correspond exactement à l'usage du produit (design/ui_kits/launcher/screen-wizard.jsx, un
/// panneau avec voile par-dessus toute la fenêtre). La vue résout <see cref="Active"/> vers son
/// contrôle via le <see cref="Prospect.Desktop.ViewLocator"/> global, donc ce service ne connaît
/// lui-même aucun type de vue. L'overlay possède le cycle de vie de ce qu'il affiche : un panneau
/// <see cref="IDisposable"/> est disposé dès qu'il cesse d'être actif, que ce soit par fermeture
/// ou par remplacement, sans que ses appelants aient à s'en soucier.
/// </summary>
public interface IOverlayService
{
    /// <summary>ViewModel du panneau actuellement affiché, ou <see langword="null"/> si aucun.</summary>
    object? Active { get; }

    /// <summary>
    /// Affiche <paramref name="overlayViewModel"/>, remplaçant le panneau actif s'il y en a un (et
    /// le disposant s'il implémente <see cref="IDisposable"/>).
    /// </summary>
    void Show(object overlayViewModel);

    /// <summary>
    /// Referme le panneau actif, le disposant s'il implémente <see cref="IDisposable"/>. Sans effet
    /// si aucun n'est affiché.
    /// </summary>
    void Close();
}