using System.Collections.ObjectModel;
using System.IO.Abstractions;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Migration;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Migration;

/// <summary>
/// Dialogue de flux d'adoption VS Launcher (design : un seul panneau qui traverse
/// <see cref="AdoptVslStep"/>, un seul panneau du début à la fin) : sélection des
/// installations et moteurs détectés par <see cref="VslDetector"/>, progression par élément,
/// rapport final groupé par catégorie. Ouvert depuis les Réglages ou depuis le rappel de premier
/// lancement de l'Accueil (voir <c>HomeViewModel.FirstRun</c>) une fois la détection déjà faite :
/// il n'y a pas d'étape de chargement, le
/// <see cref="VslDetectionResult"/> est déjà connu à la construction.
/// </summary>
/// <remarks>
/// Implémente <see cref="IProgress{T}"/> directement et
/// repasse chaque mise à jour par <see cref="IUiDispatcher.Post"/> : <see cref="VslAdoptionService.AdoptAsync"/>
/// tourne sur des continuations qui peuvent reprendre sur n'importe quel fil.
/// </remarks>
public sealed partial class AdoptVslViewModel : ObservableObject, IProgress<VslAdoptionProgress>, IDisposable
{
    private readonly VslAdoptionService _adoptionService;
    private readonly IInstalledGameVersionRepository _installedGameVersions;
    private readonly IFileSystem _fileSystem;
    private readonly IOverlayService _overlay;
    private readonly IToastService _toasts;
    private readonly IUiDispatcher _dispatcher;

    private CancellationTokenSource? _adoptionCancellation;

    public AdoptVslViewModel(
        VslDetectionResult detection,
        VslAdoptionService adoptionService,
        IInstalledGameVersionRepository installedGameVersions,
        IFileSystem fileSystem,
        IOverlayService overlay,
        IToastService toasts,
        IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(detection);
        ArgumentNullException.ThrowIfNull(adoptionService);
        ArgumentNullException.ThrowIfNull(installedGameVersions);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _adoptionService = adoptionService;
        _installedGameVersions = installedGameVersions;
        _fileSystem = fileSystem;
        _overlay = overlay;
        _toasts = toasts;
        _dispatcher = dispatcher;

        SummaryText = UiText.Migration.DetectionSummary(detection.InstallationCount, detection.GameVersionCount);

        // Toutes les installations cochées par défaut ; un moteur ne l'est que s'il manque encore
        // côté Prospect (docs de la mission : épargner un téléchargement inutile est le confort,
        // pas une redécouverte de ce qui est déjà là).
        InstallationRows = new ObservableCollection<VslInstallationSelectionRowViewModel>(
            detection.Installations.Select(installation => new VslInstallationSelectionRowViewModel(
                installation,
                ByteSizeFormatter.Format(ComputeSizeBytes(installation.Path)),
                CountMods(installation.Path),
                isSelected: true)));

        EngineRows = new ObservableCollection<VslEngineSelectionRowViewModel>(
            detection.GameVersions.Select(engine =>
            {
                var isInstalled = GameVersion.TryParse(engine.Version, out var parsed) && _installedGameVersions.IsInstalled(parsed);

                return new VslEngineSelectionRowViewModel(
                    engine,
                    ByteSizeFormatter.Format(ComputeSizeBytes(engine.Path)),
                    isInstalled,
                    isSelected: !isInstalled);
            }));
    }

    /// <summary>Levée quand l'adoption se termine (avec ou sans éléments en échec) : les pages qui listent des instances se rafraîchissent.</summary>
    public event EventHandler<VslAdoptionOutcome>? Completed;

    [ObservableProperty]
    private AdoptVslStep _step = AdoptVslStep.Selection;

    /// <summary>Résumé affiché en en-tête de l'étape de sélection (« 3 installations et 2 moteurs détectés »).</summary>
    public string SummaryText { get; }

    public ObservableCollection<VslInstallationSelectionRowViewModel> InstallationRows { get; }

    public ObservableCollection<VslEngineSelectionRowViewModel> EngineRows { get; }

    public bool HasInstallations => InstallationRows.Count > 0;

    public bool HasEngines => EngineRows.Count > 0;

    // ── Progression ─────────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _progressPhaseText = string.Empty;

    [ObservableProperty]
    private string _progressDetailText = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    // ── Rapport ─────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _reportSummaryText = string.Empty;

    public ObservableCollection<AdoptionReportGroupViewModel> ReportGroups { get; } = [];

    /// <inheritdoc />
    public void Report(VslAdoptionProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _dispatcher.Post(() => Apply(value));
    }

    private void Apply(VslAdoptionProgress value)
    {
        var phaseLabel = value.Phase == VslAdoptionPhase.AdoptingInstallations
            ? UiText.Migration.AdoptingInstallationsPhase(value.CompletedItems, value.TotalItems, value.CurrentItemLabel)
            : UiText.Migration.AdoptingEnginesPhase(value.CompletedItems, value.TotalItems, value.CurrentItemLabel);

        ProgressPhaseText = phaseLabel;

        if (value.FileProgress is { } fileProgress)
        {
            IsProgressIndeterminate = false;
            ProgressPercent = fileProgress.TotalFiles == 0 ? 0d : fileProgress.FilesCopied * 100d / fileProgress.TotalFiles;
            ProgressDetailText = UiText.Migration.FilesCopied(fileProgress.FilesCopied, fileProgress.TotalFiles);
        }
        else
        {
            IsProgressIndeterminate = true;
            ProgressDetailText = string.Empty;
        }
    }

    [RelayCommand]
    private void CancelSelection() => _overlay.Close();

    [RelayCommand]
    private void CloseReport() => _overlay.Close();

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        var selectedInstallations = InstallationRows.Where(row => row.IsSelected).Select(row => row.Source).ToArray();
        var selectedEngines = EngineRows.Where(row => row.IsSelected).Select(row => row.Source).ToArray();

        Step = AdoptVslStep.Adopting;
        ProgressPercent = 0;
        IsProgressIndeterminate = true;
        ProgressPhaseText = UiText.Migration.Starting;

        var cancellation = new CancellationTokenSource();
        _adoptionCancellation = cancellation;

        try
        {
            var outcome = await _adoptionService.AdoptAsync(selectedInstallations, selectedEngines, this, cancellation.Token).ConfigureAwait(true);
            BuildReport(outcome);
            Step = AdoptVslStep.Report;

            _toasts.Show(
                ToastTone.Success,
                UiText.Migration.CompletedToastTitle,
                UiText.Migration.CompletedToastDescription(outcome.AdoptedInstallationCount, outcome.AdoptedEngineCount));

            Completed?.Invoke(this, outcome);
        }
        catch (OperationCanceledException)
        {
            // Ce qui a déjà été adopté avant l'annulation le reste (voir la remarque de classe de
            // VslAdoptionService) : rien à montrer de plus, le dialogue se referme simplement.
            _overlay.Close();
        }
        finally
        {
            _adoptionCancellation = null;
            cancellation.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(IsAdopting))]
    private void CancelAdoption() => _adoptionCancellation?.Cancel();

    private bool IsAdopting() => Step == AdoptVslStep.Adopting;

    partial void OnStepChanged(AdoptVslStep value) => CancelAdoptionCommand.NotifyCanExecuteChanged();

    // Positifs d'abord (voix produit : mener par ce qui a marché), puis ignorées, puis échecs — le
    // ordre stable, du succès vers les échecs.
    private void BuildReport(VslAdoptionOutcome outcome)
    {
        ReportSummaryText = UiText.Migration.CompletedToastDescription(outcome.AdoptedInstallationCount, outcome.AdoptedEngineCount);
        ReportGroups.Clear();

        AddInstallationGroup(outcome, VslInstallationAdoptionStatus.Adopted, UiText.Migration.InstallationsAdoptedGroupTitle);
        AddInstallationGroup(outcome, VslInstallationAdoptionStatus.Skipped, UiText.Migration.InstallationsSkippedGroupTitle);
        AddInstallationGroup(outcome, VslInstallationAdoptionStatus.Failed, UiText.Migration.InstallationsFailedGroupTitle);

        AddEngineGroup(outcome, VslEngineAdoptionStatus.Adopted, UiText.Migration.EnginesAdoptedGroupTitle);
        AddEngineGroup(outcome, VslEngineAdoptionStatus.Skipped, UiText.Migration.EnginesSkippedGroupTitle);
        AddEngineGroup(outcome, VslEngineAdoptionStatus.Failed, UiText.Migration.EnginesFailedGroupTitle);
    }

    private void AddInstallationGroup(VslAdoptionOutcome outcome, VslInstallationAdoptionStatus status, Func<int, string> title)
    {
        var rows = outcome.Installations
            .Where(report => report.Status == status)
            .Select(report => new AdoptionReportRowViewModel(report.SourceName, report.Detail))
            .ToArray();

        if (rows.Length > 0)
        {
            ReportGroups.Add(new AdoptionReportGroupViewModel(title(rows.Length), rows));
        }
    }

    private void AddEngineGroup(VslAdoptionOutcome outcome, VslEngineAdoptionStatus status, Func<int, string> title)
    {
        var rows = outcome.Engines
            .Where(report => report.Status == status)
            .Select(report => new AdoptionReportRowViewModel(report.SourceVersion, report.Detail))
            .ToArray();

        if (rows.Length > 0)
        {
            ReportGroups.Add(new AdoptionReportGroupViewModel(title(rows.Length), rows));
        }
    }

    private long ComputeSizeBytes(string path)
        => _fileSystem.Directory.Exists(path)
            ? _fileSystem.Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(file => _fileSystem.FileInfo.New(file).Length)
            : 0L;

    private int CountMods(string installationPath)
    {
        var modsDirectory = _fileSystem.Path.Combine(installationPath, "Mods");

        return _fileSystem.Directory.Exists(modsDirectory)
            ? _fileSystem.Directory.GetFiles(modsDirectory, "*.zip", SearchOption.TopDirectoryOnly).Length
            : 0;
    }

    /// <summary>Libère le jeton d'annulation d'une adoption en cours si le dialogue est fermé pendant celui-ci.</summary>
    public void Dispose() => _adoptionCancellation?.Dispose();
}