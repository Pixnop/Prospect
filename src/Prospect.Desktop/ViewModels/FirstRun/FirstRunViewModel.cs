using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Migration;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Migration;

namespace Prospect.Desktop.ViewModels.FirstRun;

/// <summary>
/// Rappel de premier lancement (design/architecture : « checklist de premier lancement ») porté
/// par <see cref="Home.HomeViewModel"/> et affiché dans son état vide (aucune instance) : quand
/// <see cref="VslDetector"/> trouve quelque chose, une carte propose « Importer tes installations
/// VS Launcher » ; sinon la carte reste simplement masquée, l'Accueil ne montre que son état vide
/// habituel. Le flux d'import lui-même est <see cref="AdoptVslViewModel"/> (nom de code inchangé,
/// voir sa remarque de classe), identique à celui ouvert depuis les Réglages : ce ViewModel ne fait
/// que lancer la détection et l'ouvrir.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly VslDetector _detector;
    private readonly Func<VslDetectionResult, AdoptVslViewModel> _adoptFactory;
    private readonly IOverlayService _overlay;

    private VslDetectionResult? _detection;

    public FirstRunViewModel(VslDetector detector, Func<VslDetectionResult, AdoptVslViewModel> adoptFactory, IOverlayService overlay)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(adoptFactory);
        ArgumentNullException.ThrowIfNull(overlay);

        _detector = detector;
        _adoptFactory = adoptFactory;
        _overlay = overlay;
    }

    /// <summary>Levée quand une adoption ouverte depuis ce rappel se termine : l'Accueil se rafraîchit.</summary>
    public event EventHandler<VslAdoptionOutcome>? Completed;

    /// <summary>Vrai si la dernière détection a trouvé au moins une installation ou un moteur exploitable.</summary>
    [ObservableProperty]
    private bool _vslDetected;

    /// <summary>Résumé affiché sur la carte (« 3 installations et 2 moteurs détectés »).</summary>
    [ObservableProperty]
    private string _vslSummaryText = string.Empty;

    /// <summary>
    /// Lance la détection à l'emplacement par défaut de l'OS. Appelé par
    /// <see cref="Home.HomeViewModel.RefreshAsync"/> tant qu'aucune instance n'existe : sans
    /// intérêt une fois la bibliothèque peuplée, la carte ne s'affiche plus alors de toute façon.
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _detection = await _detector.DetectAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
        VslDetected = _detection.IsDetected && _detection.HasAnyContent;
        VslSummaryText = UiText.Migration.DetectionSummary(_detection.InstallationCount, _detection.GameVersionCount);
    }

    [RelayCommand]
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

    private void OnAdoptionCompleted(object? sender, VslAdoptionOutcome outcome)
    {
        if (sender is AdoptVslViewModel adopt)
        {
            adopt.Completed -= OnAdoptionCompleted;
        }

        Completed?.Invoke(this, outcome);
    }
}