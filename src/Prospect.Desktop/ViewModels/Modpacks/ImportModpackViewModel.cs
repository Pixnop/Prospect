using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.GameVersions;
using Prospect.Core.Http;
using Prospect.Core.Instances;
using Prospect.Core.Modpacks;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Modpacks;

/// <summary>
/// Dialogue de flux d'import de modpack (design : un seul panneau qui traverse
/// <see cref="ImportModpackStep"/>) : aperçu du manifest, progression par phases, rapport final
/// groupé par catégorie. Ouvert depuis l'Accueil une fois qu'un fichier a été choisi (voir
/// <see cref="Home.HomeViewModel"/>) ; le chemin de la source est donc déjà connu à la construction,
/// contrairement à <see cref="Dialogs.DuplicateDialogViewModel"/> qui part d'un formulaire vide.
/// </summary>
/// <remarks>
/// Implémente <see cref="IProgress{T}"/> directement, comme <c>WizardViewModel</c> pour
/// l'installation de version : <see cref="ModpackImportService.ImportAsync"/> tourne sur des
/// continuations qui peuvent reprendre sur n'importe quel fil, donc chaque mise à jour d'état
/// observable repasse par <see cref="IUiDispatcher.Post"/> plutôt que de faire confiance à un
/// éventuel <see cref="System.Threading.SynchronizationContext"/> ambiant.
/// </remarks>
public sealed partial class ImportModpackViewModel : ObservableObject, IProgress<ModpackImportProgress>, IDisposable
{
    private readonly string _sourcePath;
    private readonly ModpackImportService _importService;
    private readonly IOverlayService _overlay;
    private readonly IToastService _toasts;
    private readonly IUiDispatcher _dispatcher;

    private ModpackImportPreview? _preview;
    private CancellationTokenSource? _importCancellation;

    /// <summary>Construit le dialogue pour une source déjà choisie par l'utilisateur.</summary>
    /// <param name="sourcePath">Manifest <c>.json</c> ou archive <c>.zip</c> choisi via le sélecteur de fichier.</param>
    /// <param name="importService">Service Core d'import.</param>
    /// <param name="overlay">Panneau modal, pour se refermer.</param>
    /// <param name="toasts">Toast de succès une fois l'instance créée.</param>
    /// <param name="dispatcher">Fil UI, pour les mises à jour de progression venues d'un fil de fond.</param>
    public ImportModpackViewModel(
        string sourcePath,
        ModpackImportService importService,
        IOverlayService overlay,
        IToastService toasts,
        IUiDispatcher dispatcher)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentNullException.ThrowIfNull(importService);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _sourcePath = sourcePath;
        _importService = importService;
        _overlay = overlay;
        _toasts = toasts;
        _dispatcher = dispatcher;
    }

    /// <summary>Levé une fois l'instance créée (avec ou sans mods en échec) : l'Accueil se rafraîchit.</summary>
    public event EventHandler<InstanceRecord>? Imported;

    [ObservableProperty]
    private ImportModpackStep _step = ImportModpackStep.Loading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    // ── Aperçu ──────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _packName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _instanceName = string.Empty;

    [ObservableProperty]
    private string _previewSubtitle = string.Empty;

    [ObservableProperty]
    private bool _showGameVersionWarning;

    [ObservableProperty]
    private string _gameVersionWarningText = string.Empty;

    [ObservableProperty]
    private bool _showModConfigNotice;

    public string? InstanceNameError => string.IsNullOrWhiteSpace(InstanceName) ? UiText.Wizard.NameRequired : null;

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

    public ObservableCollection<ImportReportGroupViewModel> ReportGroups { get; } = [];

    /// <inheritdoc />
    public void Report(ModpackImportProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _dispatcher.Post(() =>
        {
            if (value.Phase == ModpackImportPhase.InstallingGameVersion)
            {
                ApplyGameVersionProgress(value.GameVersionProgress);
            }
            else
            {
                IsProgressIndeterminate = false;
                ProgressPercent = value.TotalMods == 0 ? 0d : value.CompletedMods * 100d / value.TotalMods;
                ProgressPhaseText = UiText.Modpacks.InstallingModsPhase(value.CompletedMods, value.TotalMods, value.CurrentModId);
                ProgressDetailText = string.Empty;
            }
        });
    }

    [RelayCommand]
    private async Task LoadPreviewAsync()
    {
        try
        {
            var preview = await _importService.PreviewAsync(_sourcePath, CancellationToken.None).ConfigureAwait(true);
            _preview = preview;

            PackName = preview.Manifest.Name;
            InstanceName = preview.Manifest.Name;
            PreviewSubtitle = UiText.Modpacks.ImportPreviewSubtitle(preview.Manifest.GameVersion.ToString(), preview.Manifest.Mods.Count);

            ShowGameVersionWarning = !preview.GameVersionInstalled;
            GameVersionWarningText = ShowGameVersionWarning
                ? UiText.Modpacks.ImportGameVersionWarning(preview.Manifest.GameVersion.ToString(), preview.GameVersionDownloadSize ?? string.Empty)
                : string.Empty;

            ShowModConfigNotice = preview.HasModConfig;

            Step = ImportModpackStep.Preview;
        }
        catch (Exception exception) when (exception is ModpackSourceInvalidException or ModpackManifestInvalidException)
        {
            ErrorMessage = exception.Message;
            Step = ImportModpackStep.Failed;
        }
    }

    [RelayCommand]
    private void CancelPreview() => _overlay.Close();

    [RelayCommand]
    private void CloseFailed() => _overlay.Close();

    [RelayCommand]
    private void CloseReport() => _overlay.Close();

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (_preview is not { } preview)
        {
            return;
        }

        Step = ImportModpackStep.Importing;
        ProgressPhaseText = ShowGameVersionWarning ? UiText.Modpacks.InstallingGameVersionPhase : UiText.Modpacks.InstallingModsPhase(0, preview.Manifest.Mods.Count, null);
        ProgressPercent = 0;
        IsProgressIndeterminate = ShowGameVersionWarning;

        var cancellation = new CancellationTokenSource();
        _importCancellation = cancellation;

        try
        {
            var outcome = await _importService.ImportAsync(preview, InstanceName.Trim(), this, cancellation.Token).ConfigureAwait(true);
            BuildReport(outcome);
            Step = ImportModpackStep.Report;

            _toasts.Show(
                ToastTone.Success,
                UiText.Modpacks.ImportedToastTitle(outcome.Instance.Metadata.Name),
                UiText.Modpacks.ImportedToastDescription(outcome.InstalledCount, outcome.Mods.Count));

            Imported?.Invoke(this, outcome.Instance);
        }
        catch (OperationCanceledException)
        {
            // L'instance partiellement importée a déjà été supprimée par le service (voir sa
            // remarque de classe) : rien à montrer, le dialogue se referme simplement.
            _overlay.Close();
        }
        catch (Exception exception) when (exception is InstanceNameInvalidException or GameVersionNotAvailableException
                                               or DownloadFailedException or GameInstallFailedException)
        {
            ErrorMessage = exception.Message;
            Step = ImportModpackStep.Failed;
        }
        finally
        {
            _importCancellation = null;
            cancellation.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(IsImporting))]
    private void CancelImport() => _importCancellation?.Cancel();

    private bool IsImporting() => Step == ImportModpackStep.Importing;

    private bool CanConfirm() => InstanceNameError is null;

    partial void OnInstanceNameChanged(string value) => OnPropertyChanged(nameof(InstanceNameError));

    partial void OnStepChanged(ImportModpackStep value) => CancelImportCommand.NotifyCanExecuteChanged();

    private void ApplyGameVersionProgress(GameInstallProgress? progress)
    {
        ProgressPhaseText = UiText.Modpacks.InstallingGameVersionPhase;

        if (progress is null)
        {
            return;
        }

        IsProgressIndeterminate = progress.Ratio is null;
        ProgressPercent = (progress.Ratio ?? 0d) * 100d;
        ProgressDetailText = GameInstallProgressPresenter.DetailText(progress);
    }

    // Positifs d'abord (voix produit : mener par ce qui a marché), puis les catégories d'échec dans
    // le même ordre que documenté par le Core (introuvable, version absente, empreinte, réseau).
    private static readonly ModpackModImportStatus[] ReportOrder =
    [
        ModpackModImportStatus.Installed,
        ModpackModImportStatus.NotFound,
        ModpackModImportStatus.VersionMissing,
        ModpackModImportStatus.Sha256Mismatch,
        ModpackModImportStatus.NetworkFailure,
    ];

    private void BuildReport(ModpackImportOutcome outcome)
    {
        ReportSummaryText = UiText.Modpacks.ImportedToastDescription(outcome.InstalledCount, outcome.Mods.Count);

        ReportGroups.Clear();
        foreach (var status in ReportOrder)
        {
            var rows = outcome.Mods
                .Where(mod => mod.Status == status)
                .Select(mod => new ImportReportRowViewModel(mod))
                .ToArray();

            if (rows.Length == 0)
            {
                continue;
            }

            ReportGroups.Add(new ImportReportGroupViewModel(UiText.Modpacks.ReportGroupTitle(status, rows.Length), rows));
        }
    }

    /// <summary>Libère le jeton d'annulation d'un import en cours si le dialogue est fermé pendant celui-ci.</summary>
    public void Dispose() => _importCancellation?.Dispose();
}