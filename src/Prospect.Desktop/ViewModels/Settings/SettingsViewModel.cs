using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Migration;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Migration;

namespace Prospect.Desktop.ViewModels.Settings;

/// <summary>
/// Écran Réglages (docs/architecture.md liste plusieurs sections futures — Jeu, Réseau, Comptes,
/// À propos — non construites ici : seule la section Général existe, avec l'action d'adoption VS
/// Launcher accessible en permanence). Même flux que <see cref="Home.HomeViewModel.FirstRun"/>
/// (même dialogue <see cref="AdoptVslViewModel"/>, même service de détection) mais TOUJOURS
/// disponible ici, avec un choix de dossier manuel si la détection automatique ne trouve rien à
/// l'emplacement par défaut de l'OS.
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
        HomeViewModel home)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(adoptFactory);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(home);

        _detector = detector;
        _adoptFactory = adoptFactory;
        _overlay = overlay;
        _filePicker = filePicker;
        _home = home;
    }

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