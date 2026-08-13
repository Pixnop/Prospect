using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Downloads;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Settings;
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
        ModBrowserViewModel modBrowser,
        DownloadsViewModel downloads,
        SettingsViewModel settings,
        Func<string, InstanceDetailViewModel> instanceDetailFactory,
        IOverlayService overlay,
        IToastService toasts,
        IAppEnvironment appEnvironment)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(modBrowser);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(instanceDetailFactory);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(appEnvironment);

        Home = home;
        Versions = versions;
        ModBrowser = modBrowser;
        Downloads = downloads;
        Settings = settings;
        _instanceDetailFactory = instanceDetailFactory;
        Overlay = overlay;
        Toasts = toasts;
        UseCustomTitlebar = ResolveUseCustomTitlebar(appEnvironment.CurrentOperatingSystem);
        Home.InstanceOpenRequested += (_, slug) => ShowInstanceDetail(slug);
        Settings.FirstRun.NavigateToVersionsRequested += (_, _) => ShowVersions();
        Settings.FirstRun.NavigateToAccountSettingsRequested += (_, _) => ShowAccountSettings();
        Settings.FirstRun.VslAdopted += (_, _) => Home.RefreshCommand.Execute(null);

        var homeNavItem = new NavItemViewModel("layers", UiText.Shell.NavHome, home, Navigate);
        var modsNavItem = new NavItemViewModel("package", UiText.Shell.NavMods, modBrowser, ShowModBrowser);
        var versionsNavItem = new NavItemViewModel("hard-drive", UiText.Shell.NavVersions, versions, ShowVersions);
        SettingsNavItem = new NavItemViewModel("settings", UiText.Shell.NavSettings, settings, ShowSettings);

        LibraryNavItems = [homeNavItem, modsNavItem, versionsNavItem];
        _allNavItems.AddRange(LibraryNavItems);
        _allNavItems.Add(SettingsNavItem);

        _currentPage = null!;
        Navigate(home);

        // Angle mort corrigé (bug préexistant débusqué en préparant le chantier Réglages) : l'Accueil
        // est la seule page dont la navigation initiale passe directement par Navigate() ci-dessus
        // plutôt que par un ShowXxx dédié — ShowSettings et ShowModBrowser, eux, enchaînent toujours
        // leur InitializeCommand juste après avoir navigué (voir plus bas). Sans l'appel qui suit,
        // RIEN, nulle part dans l'application, n'appelait jamais HomeViewModel.RefreshCommand au
        // démarrage réel : les tests headless appelaient tous ce rafraîchissement à la main avant
        // d'affirmer quoi que ce soit, ce qui masquait totalement l'angle mort. Un utilisateur avec
        // des instances déjà installées ouvrait donc l'application sur une grille vide. Fire-and-forget
        // volontaire (même idiome que ShowSettings/ShowModBrowser plus bas) : le constructeur ne peut
        // pas être asynchrone, et HomeViewModel.RefreshAsync se rejoint proprement si quelqu'un
        // d'autre (test, ou un futur bouton Actualiser) appelle RefreshCommand pendant que ce premier
        // scan tourne encore (voir la docstring de HomeViewModel.RefreshCommand).
        _ = Home.RefreshCommand.ExecuteAsync(null);
    }

    public HomeViewModel Home { get; }

    /// <summary>Page « Versions du jeu ».</summary>
    public VersionsViewModel Versions { get; }

    /// <summary>Page « Navigateur de mods ».</summary>
    public ModBrowserViewModel ModBrowser { get; }

    /// <summary>Page « Réglages » (docs/architecture.md : seule la section Générale existe pour l'instant).</summary>
    public SettingsViewModel Settings { get; }

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
    /// Ouvre les Réglages directement sur la section Comptes : cible de la ligne « Compte Vintage
    /// Story » de la checklist de premier lancement. Passe par le même <see cref="ShowSettings"/>
    /// que l'entrée de sidebar (donc même relance de détection VS Launcher), puis sélectionne
    /// l'onglet, plutôt que de laisser l'utilisateur le chercher.
    /// </summary>
    public void ShowAccountSettings()
    {
        ShowSettings(Settings);
        Settings.SelectTabCommand.Execute(SettingsTab.Accounts);
    }

    /// <summary>
    /// Affiche l'écran de premier lancement s'il n'a jamais été vu (voir
    /// <see cref="ViewModels.FirstRun.FirstRunScreenViewModel.HasBeenSeen"/>), sur le panneau modal
    /// partagé. Appelée une seule fois par <c>App.OnFrameworkInitializationCompleted</c>, juste
    /// après la construction de la fenêtre principale — JAMAIS un effet de bord automatique du
    /// constructeur (même principe que <c>ThemeService.ApplyStartupTheme</c>, voir sa docstring) :
    /// les dizaines de tests headless qui résolvent ce ViewModel depuis le conteneur sans simuler un
    /// vrai démarrage d'application ne doivent pas se retrouver avec cet écran ouvert par surprise.
    /// </summary>
    public void ShowFirstRunIfNeeded()
    {
        if (Settings.FirstRun.HasBeenSeen)
        {
            return;
        }

        Overlay.Show(Settings.FirstRun);
        _ = Settings.FirstRun.InitializeCommand.ExecuteAsync(null);
    }

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
        detail.NavigateToVersionsRequested += (_, _) => ShowVersions();
        detail.BrowseModsRequested += (_, instanceSlug) => ShowModBrowser(instanceSlug);

        Navigate(detail);
        _ = detail.InitializeCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Ouvre le navigateur de mods, éventuellement préfiltré sur une instance (bouton « Parcourir
    /// le ModDB » de l'onglet Mods). Le rechargement est déclenché à chaque entrée sur la page :
    /// le catalogue a son propre cache, l'appel ne coûte rien tant qu'il est frais.
    /// </summary>
    public void ShowModBrowser(string? instanceSlug = null)
    {
        if (instanceSlug is not null)
        {
            ModBrowser.PendingInstanceSlug = instanceSlug;
        }

        ShowModBrowser((object)ModBrowser);
    }

    private void ShowModBrowser(object page)
    {
        Navigate(page);
        _ = ModBrowser.InitializeCommand.ExecuteAsync(null);
    }

    // Même construction que ShowModBrowser : la détection VS Launcher doit être relancée à chaque
    // entrée sur la page, pas seulement à la construction du conteneur (l'utilisateur a pu
    // installer VS Launcher, ou changer de dossier, entre deux visites des Réglages).
    private void ShowSettings(object page)
    {
        Navigate(page);
        _ = Settings.InitializeCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Ouvre l'écran Versions ET déclenche son chargement, comme <see cref="ShowModBrowser(object)"/>
    /// et <see cref="ShowSettings"/> le font pour les leurs.
    /// </summary>
    /// <remarks>
    /// Exactement l'angle mort déjà corrigé sur l'Accueil, et il avait survécu ici : l'écran Versions
    /// a TROIS entrées (l'item de la sidebar, la checklist de premier lancement, l'action
    /// « Installer » du docteur d'instance via le détail d'instance) et les trois se contentaient de
    /// <see cref="Navigate"/>. RIEN n'appelait donc jamais son chargement, et la seule façon de
    /// remplir l'écran était le bouton « Vérifier les nouveautés ». Les tests headless appelaient
    /// tous <c>RefreshCommand</c> à la main juste après avoir navigué : la navigation était réelle,
    /// le rafraîchissement manuel masquait l'absence de déclencheur — le même masquage, mot pour mot,
    /// que celui qui avait caché le défaut de l'Accueil.
    ///
    /// Rechargement à CHAQUE entrée plutôt qu'une seule fois, comme le navigateur de mods : le scan
    /// du disque est local et doit rester frais (une version peut avoir été installée depuis le
    /// wizard entre deux visites), et le catalogue distant a son propre cache, donc l'appel ne coûte
    /// aucune requête tant qu'il est frais. Fire-and-forget, et jamais deux chargements concurrents :
    /// <see cref="VersionsViewModel"/> les met en file (voir son sémaphore).
    /// </remarks>
    public void ShowVersions() => ShowVersions(Versions);

    private void ShowVersions(object page)
    {
        Navigate(page);
        _ = Versions.RefreshCommand.ExecuteAsync(null);
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