using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Common;
using Prospect.Desktop.ViewModels.Home;

namespace Prospect.Desktop.ViewModels.Shell;

/// <summary>
/// ViewModel racine du shell applicatif (design/ui_kits/launcher/app-shell.jsx) : expose la page
/// courante et la navigation (aucun framework tiers, voir docs/architecture.md), le popover
/// Téléchargements (vide pour cette PR, le DownloadManager arrive plus tard) et les services
/// partagés (panneau modal, toasts) que les pages consomment par injection de constructeur.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly List<NavItemViewModel> _allNavItems = [];

    public ShellViewModel(HomeViewModel home, IOverlayService overlay, IToastService toasts, IAppEnvironment appEnvironment)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(appEnvironment);

        Home = home;
        Overlay = overlay;
        Toasts = toasts;
        UseCustomTitlebar = ResolveUseCustomTitlebar(appEnvironment.CurrentOperatingSystem);

        var modsPage = new PlaceholderPageViewModel(
            "package",
            "Navigateur de mods",
            "Bientôt disponible : recherche et installation de mods depuis le ModDB officiel.");
        var versionsPage = new PlaceholderPageViewModel(
            "hard-drive",
            "Versions du jeu",
            "Bientôt disponible : téléchargement et gestion des versions du jeu installées.");
        var settingsPage = new PlaceholderPageViewModel(
            "settings",
            "Réglages",
            "Bientôt disponible : préférences générales, jeu, réseau et comptes.");

        var homeNavItem = new NavItemViewModel("layers", "Accueil", home, Navigate);
        var modsNavItem = new NavItemViewModel("package", "Mods", modsPage, Navigate);
        var versionsNavItem = new NavItemViewModel("hard-drive", "Versions", versionsPage, Navigate);
        SettingsNavItem = new NavItemViewModel("settings", "Réglages", settingsPage, Navigate);

        LibraryNavItems = [homeNavItem, modsNavItem, versionsNavItem];
        _allNavItems.AddRange(LibraryNavItems);
        _allNavItems.Add(SettingsNavItem);

        _currentPage = null!;
        Navigate(home);
    }

    public HomeViewModel Home { get; }

    /// <summary>Panneau modal partagé (wizard, dialogues de carte) : la vue résout son contenu via le <see cref="Prospect.Desktop.ViewLocator"/>.</summary>
    public IOverlayService Overlay { get; }

    public IToastService Toasts { get; }

    /// <summary>Section « Bibliothèque » de la sidebar : Accueil, Mods, Versions.</summary>
    public IReadOnlyList<NavItemViewModel> LibraryNavItems { get; }

    /// <summary>Entrée « Réglages » de la zone basse de la sidebar (avec Téléchargements, géré à part car il ouvre un popover et non une page).</summary>
    public NavItemViewModel SettingsNavItem { get; }

    /// <summary>
    /// Unique point de bascule vers les décorations natives (docs/architecture.md) : vrai sur les
    /// trois OS aujourd'hui. La fenêtre applicative l'applique à ses propres propriétés de
    /// décoration et masque sa <c>TitlebarView</c> quand il est faux ; à rebasculer ici pour Linux
    /// si Wayland pose un problème réel, sans toucher au reste du shell.
    /// </summary>
    public bool UseCustomTitlebar { get; }

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private bool _isDownloadsPopoverOpen;

    [RelayCommand]
    private void ToggleDownloadsPopover() => IsDownloadsPopoverOpen = !IsDownloadsPopoverOpen;

    [RelayCommand]
    private void CloseDownloadsPopover() => IsDownloadsPopoverOpen = false;

    private void Navigate(object page)
    {
        CurrentPage = page;
        IsDownloadsPopoverOpen = false;
        foreach (var item in _allNavItems)
        {
            item.IsActive = ReferenceEquals(item.Page, page);
        }
    }

    private static bool ResolveUseCustomTitlebar(AppOperatingSystem operatingSystem) => operatingSystem switch
    {
        AppOperatingSystem.Windows => true,
        AppOperatingSystem.MacOs => true,
        AppOperatingSystem.Linux => true, // à rebasculer sur false si Wayland pose problème, voir docs/architecture.md
        _ => true,
    };
}