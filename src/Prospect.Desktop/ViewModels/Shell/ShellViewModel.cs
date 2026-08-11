using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Common;
using Prospect.Desktop.ViewModels.Downloads;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Versions;

namespace Prospect.Desktop.ViewModels.Shell;

/// <summary>
/// ViewModel racine du shell applicatif (design/ui_kits/launcher/app-shell.jsx) : expose la page
/// courante et la navigation (aucun framework tiers, voir docs/architecture.md), le popover
/// Téléchargements branché sur la file du DownloadManager, et les services partagés (panneau
/// modal, toasts) que les pages consomment par injection de constructeur.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly List<NavItemViewModel> _allNavItems = [];
    private readonly Func<string, InstanceDetailViewModel> _instanceDetailFactory;

    public ShellViewModel(
        HomeViewModel home,
        VersionsViewModel versions,
        DownloadsViewModel downloads,
        Func<string, InstanceDetailViewModel> instanceDetailFactory,
        IOverlayService overlay,
        IToastService toasts,
        IAppEnvironment appEnvironment)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(instanceDetailFactory);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(appEnvironment);

        Home = home;
        Versions = versions;
        Downloads = downloads;
        _instanceDetailFactory = instanceDetailFactory;
        Overlay = overlay;
        Toasts = toasts;
        UseCustomTitlebar = ResolveUseCustomTitlebar(appEnvironment.CurrentOperatingSystem);
        Home.InstanceOpenRequested += (_, slug) => ShowInstanceDetail(slug);

        var modsPage = new PlaceholderPageViewModel(
            "package",
            "Navigateur de mods",
            "Bientôt disponible : recherche et installation de mods depuis le ModDB officiel.");
        var settingsPage = new PlaceholderPageViewModel(
            "settings",
            "Réglages",
            "Bientôt disponible : préférences générales, jeu, réseau et comptes.");

        var homeNavItem = new NavItemViewModel("layers", "Accueil", home, Navigate);
        var modsNavItem = new NavItemViewModel("package", "Mods", modsPage, Navigate);
        var versionsNavItem = new NavItemViewModel("hard-drive", "Versions", versions, Navigate);
        SettingsNavItem = new NavItemViewModel("settings", "Réglages", settingsPage, Navigate);

        LibraryNavItems = [homeNavItem, modsNavItem, versionsNavItem];
        _allNavItems.AddRange(LibraryNavItems);
        _allNavItems.Add(SettingsNavItem);

        _currentPage = null!;
        Navigate(home);
    }

    public HomeViewModel Home { get; }

    /// <summary>Page « Versions du jeu ».</summary>
    public VersionsViewModel Versions { get; }

    /// <summary>Contenu du popover Téléchargements : une vue sur la file du DownloadManager.</summary>
    public DownloadsViewModel Downloads { get; }

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

    /// <summary>Retour à l'Accueil : cible du bouton retour de la page de détail.</summary>
    public void ShowHome() => Navigate(Home);

    /// <summary>
    /// Ouvre la page de détail de l'instance <paramref name="slug"/> (clic sur une carte
    /// d'Accueil, hors bouton Jouer/Arrêter et menu — voir <see cref="HomeViewModel.InstanceOpenRequested"/>).
    /// La page est transient (une nouvelle instance de ViewModel à chaque ouverture, voir
    /// <see cref="Prospect.Desktop.CompositionRoot"/>) : <see cref="Navigate"/> se charge de
    /// disposer celle qu'elle remplace.
    /// </summary>
    public void ShowInstanceDetail(string slug)
    {
        var detail = _instanceDetailFactory(slug);
        detail.BackRequested += (_, _) => ShowHome();
        detail.NavigateToVersionsRequested += (_, _) => Navigate(Versions);

        Navigate(detail);
        _ = detail.InitializeCommand.ExecuteAsync(null);
    }

    // Dispose la page sortante si elle en a besoin (InstanceDetailViewModel se désabonne de
    // RunningInstanceTracker) : sans ça, chaque instance ouverte puis quittée resterait suivie
    // indéfiniment par le tracker, qui est un singleton pour toute la durée de l'application.
    private void Navigate(object page)
    {
        if (!ReferenceEquals(CurrentPage, page) && CurrentPage is IDisposable outgoing)
        {
            outgoing.Dispose();
        }

        CurrentPage = page;
        IsDownloadsPopoverOpen = false;
        foreach (var item in _allNavItems)
        {
            item.IsActive = ReferenceEquals(item.Page, page);
        }
    }

    // Table plutôt qu'un switch/if : les trois OS valent aujourd'hui vrai (voir la docstring
    // d'UseCustomTitlebar), et une table de correspondance par OS rend ça lisible comme un
    // réglage plutôt que comme une branche conditionnelle qui semble avoir oublié de différencier
    // ses cas.
    private static readonly IReadOnlyDictionary<AppOperatingSystem, bool> CustomTitlebarByOperatingSystem = new Dictionary<AppOperatingSystem, bool>
    {
        [AppOperatingSystem.Windows] = true,
        [AppOperatingSystem.MacOs] = true,
        [AppOperatingSystem.Linux] = true, // à rebasculer sur false si Wayland pose problème, voir docs/architecture.md
    };

    private static bool ResolveUseCustomTitlebar(AppOperatingSystem operatingSystem)
        => CustomTitlebarByOperatingSystem.GetValueOrDefault(operatingSystem, true);
}