using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Instances;
using Prospect.Core.Modpacks;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Modpacks;

/// <summary>
/// Dialogue « Exporter » ouvert depuis l'en-tête de la page de détail d'instance : forme (manifest
/// seul ou archive), inclusion de <c>ModConfig/</c>, sélecteur de fichier de destination, puis soit
/// une fermeture immédiate avec toast de succès, soit — s'il y a des mods laissés de côté — un
/// second temps dans ce même dialogue qui les liste avant de se refermer. Même esprit que
/// <see cref="Dialogs.DuplicateDialogViewModel"/> : une seule conversation avec l'utilisateur.
/// </summary>
public sealed partial class ExportModpackDialogViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly ModpackExportService _exportService;
    private readonly IFilePickerService _filePicker;
    private readonly IOverlayService _overlay;
    private readonly IToastService _toasts;
    private readonly string _suggestedFileStem;

    /// <summary>Construit le dialogue.</summary>
    /// <param name="slug">Instance à exporter.</param>
    /// <param name="instanceName">Nom affiché de l'instance, pour le titre et le nom de fichier proposé.</param>
    /// <param name="exportService">Service Core d'export.</param>
    /// <param name="filePicker">Sélecteur de fichier de destination.</param>
    /// <param name="overlay">Panneau modal, pour se refermer.</param>
    /// <param name="toasts">Toast de succès.</param>
    public ExportModpackDialogViewModel(
        string slug,
        string instanceName,
        ModpackExportService exportService,
        IFilePickerService filePicker,
        IOverlayService overlay,
        IToastService toasts)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentException.ThrowIfNullOrEmpty(instanceName);
        ArgumentNullException.ThrowIfNull(exportService);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);

        _slug = slug;
        _exportService = exportService;
        _filePicker = filePicker;
        _overlay = overlay;
        _toasts = toasts;
        _suggestedFileStem = InstanceSlugGenerator.Slugify(instanceName);

        Title = UiText.Modpacks.ExportTitle(instanceName);
    }

    public string Title { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManifestOnly))]
    [NotifyPropertyChangedFor(nameof(IsArchive))]
    [NotifyPropertyChangedFor(nameof(ShowModConfigOption))]
    private ModpackExportFormat _selectedFormat = ModpackExportFormat.Archive;

    public bool IsManifestOnly => SelectedFormat == ModpackExportFormat.ManifestOnly;

    public bool IsArchive => SelectedFormat == ModpackExportFormat.Archive;

    /// <summary>La case ModConfig n'a de sens que pour une archive : un .json seul ne peut porter que le manifest.</summary>
    public bool ShowModConfigOption => IsArchive;

    [ObservableProperty]
    private bool _includeModConfig = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isResultPhase;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _modsExportedCount;

    public ObservableCollection<ExportSkippedModRowViewModel> SkippedMods { get; } = [];

    public bool HasSkippedMods => SkippedMods.Count > 0;

    [RelayCommand]
    private void SelectFormat(ModpackExportFormat format) => SelectedFormat = format;

    [RelayCommand]
    private void Cancel() => _overlay.Close();

    [RelayCommand]
    private void CloseResult() => _overlay.Close();

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var format = SelectedFormat;
        var extension = format == ModpackExportFormat.Archive ? "zip" : "json";
        var destination = await _filePicker
            .PickSaveFileAsync(UiText.Modpacks.ExportPickerTitle, $"{_suggestedFileStem}.{extension}", extension)
            .ConfigureAwait(true);

        if (destination is null)
        {
            // Sélecteur annulé par l'utilisateur : retour silencieux au formulaire, ce n'est pas une erreur.
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var options = new ModpackExportOptions(format, IncludeModConfig && format == ModpackExportFormat.Archive);
            var result = await _exportService.ExportAsync(_slug, destination, options, CancellationToken.None).ConfigureAwait(true);

            ModsExportedCount = result.ModsExported;
            SkippedMods.Clear();
            foreach (var skipped in result.SkippedMods)
            {
                SkippedMods.Add(new ExportSkippedModRowViewModel(skipped));
            }

            _toasts.Show(ToastTone.Success, UiText.Toasts.ModpackExportedTitle, UiText.Modpacks.ExportedToastDescription(result.ModsExported));

            if (HasSkippedMods)
            {
                IsResultPhase = true;
            }
            else
            {
                _overlay.Close();
            }
        }
        catch (Exception exception) when (exception is InstanceNotFoundException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExport() => !IsBusy;
}

/// <summary>Une ligne de la liste des mods laissés de côté par un export.</summary>
public sealed class ExportSkippedModRowViewModel
{
    /// <summary>Construit la ligne.</summary>
    public ExportSkippedModRowViewModel(ModpackExportSkippedMod skipped)
    {
        ArgumentNullException.ThrowIfNull(skipped);

        FileName = skipped.FileName;
        ReasonText = UiText.Modpacks.ExportSkipReason(skipped.Reason);
    }

    public string FileName { get; }

    public string ReasonText { get; }
}