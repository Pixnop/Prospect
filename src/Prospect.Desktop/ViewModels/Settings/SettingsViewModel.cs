using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.Migration;
using Prospect.Core.Settings;
using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.FirstRun;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Migration;

namespace Prospect.Desktop.ViewModels.Settings;

/// <summary>
/// Écran Réglages (design/ui_kits/launcher/screen-settings.jsx) : cinq sections en onglets —
/// Général (thème, langue, revoir le premier lancement, et l'action d'adoption VS Launcher,
/// toujours accessible ici), Jeu (emplacement des données), Réseau (téléchargements simultanés),
/// Comptes (état vide, le chantier compte est le prochain) et À propos (version, licence, dépôt,
/// site officiel). Même flux d'adoption que <see cref="Home.HomeViewModel.FirstRun"/> (même
/// dialogue <see cref="AdoptVslViewModel"/>, même service de détection) mais TOUJOURS disponible
/// ici, avec un choix de dossier manuel si la détection automatique ne trouve rien à l'emplacement
/// par défaut de l'OS.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly VslDetector _detector;
    private readonly Func<VslDetectionResult, AdoptVslViewModel> _adoptFactory;
    private readonly IOverlayService _overlay;
    private readonly IFilePickerService _filePicker;
    private readonly HomeViewModel _home;

    private VslDetectionResult? _detection;

    public SettingsViewModel(
        VslDetector detector,
        Func<VslDetectionResult, AdoptVslViewModel> adoptFactory,
        IOverlayService overlay,
        IFilePickerService filePicker,
        HomeViewModel home,
        FirstRunScreenViewModel firstRun,
        SettingsService settings,
        AppPaths appPaths,
        IExternalUrlOpener urlOpener)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(adoptFactory);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(firstRun);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(urlOpener);

        _detector = detector;
        _adoptFactory = adoptFactory;
        _overlay = overlay;
        _filePicker = filePicker;
        _home = home;
        FirstRun = firstRun;

        General = new SettingsGeneralViewModel(settings);
        Game = new SettingsGameViewModel(appPaths, urlOpener);
        Network = new SettingsNetworkViewModel(settings);
        About = new SettingsAboutViewModel(urlOpener);
    }

    /// <summary>
    /// Écran de premier lancement (même instance singleton que celle affichée automatiquement au
    /// tout premier démarrage, voir <see cref="Shell.ShellViewModel.ShowFirstRunIfNeeded"/>) :
    /// « Revoir le premier lancement » ci-dessous l'ouvre à nouveau, quel que soit
    /// <see cref="FirstRunScreenViewModel.HasBeenSeen"/>.
    /// </summary>
    public FirstRunScreenViewModel FirstRun { get; }

    /// <summary>Rouvre l'écran de premier lancement (section Général, « Revoir le premier lancement »).</summary>
    [RelayCommand]
    private async Task ReplayFirstRunAsync()
    {
        await FirstRun.InitializeCommand.ExecuteAsync(null).ConfigureAwait(true);
        _overlay.Show(FirstRun);
    }

    /// <summary>Section Général : thème, langue, carte d'adoption VS Launcher (état porté par ce ViewModel-ci).</summary>
    public SettingsGeneralViewModel General { get; }

    /// <summary>Section Jeu : emplacement des données.</summary>
    public SettingsGameViewModel Game { get; }

    /// <summary>Section Réseau : téléchargements simultanés.</summary>
    public SettingsNetworkViewModel Network { get; }

    /// <summary>Section À propos : version, licence, dépôt.</summary>
    public SettingsAboutViewModel About { get; }

    // Général en premier, comme la maquette (useState('Général')) : ordre et sélection par défaut
    // alignés sur design/ui_kits/launcher/screen-settings.jsx.
    [ObservableProperty]
    private SettingsTab _selectedTab = SettingsTab.General;

    [RelayCommand]
    private void SelectTab(SettingsTab tab) => SelectedTab = tab;

    [ObservableProperty]
    private bool _isDetecting;

    /// <summary>Vrai si la dernière détection (par défaut ou dossier choisi) a trouvé quelque chose d'exploitable.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenAdoptionCommand))]
    private bool _vslDetected;

    /// <summary>Résumé de la détection, ou message « rien trouvé » à défaut.</summary>
    [ObservableProperty]
    private string _vslStatusText = string.Empty;

    /// <summary>
    /// Lance la détection au chemin par défaut de l'OS. Contrairement à
    /// <see cref="FirstRun.FirstRunViewModel"/>, cette page est accessible en permanence : rien ne
    /// conditionne l'appel à un état vide de l'Accueil.
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        IsDetecting = true;
        try
        {
            await DetectAsync(rootOverride: null, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsDetecting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(VslDetected))]
    private void OpenAdoption()
    {
        if (_detection is not { } detection)
        {
            return;
        }

        var adopt = _adoptFactory(detection);
        adopt.Completed += OnAdoptionCompleted;
        _overlay.Show(adopt);
    }

    /// <summary>
    /// Choix manuel d'un dossier (« Choisir un dossier… ») : pour l'utilisateur dont VS Launcher
    /// vit à un emplacement non standard (installation portable, disque externe...), voir
    /// <see cref="VslDetector.DetectAsync"/>. Relance la détection sur ce dossier précis, sans
    /// retomber sur le chemin par défaut de l'OS si rien n'y est trouvé.
    /// </summary>
    [RelayCommand]
    private async Task PickFolderAsync()
    {
        var path = await _filePicker.PickFolderAsync(UiText.Settings.PickFolderTitle).ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        IsDetecting = true;
        try
        {
            await DetectAsync(path, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsDetecting = false;
        }
    }

    private async Task DetectAsync(string? rootOverride, CancellationToken cancellationToken)
    {
        var detection = await _detector.DetectAsync(rootOverride, cancellationToken).ConfigureAwait(true);
        _detection = detection;
        VslDetected = detection.IsDetected && detection.HasAnyContent;
        VslStatusText = VslDetected
            ? UiText.Migration.DetectionSummary(detection.InstallationCount, detection.GameVersionCount)
            : UiText.Settings.VslNotDetected;
    }

    private void OnAdoptionCompleted(object? sender, VslAdoptionOutcome outcome)
    {
        if (sender is AdoptVslViewModel adopt)
        {
            adopt.Completed -= OnAdoptionCompleted;
        }

        _ = _home.RefreshCommand.ExecuteAsync(null);
    }
}