using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Diagnostics;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Logs;

/// <summary>
/// Page « Journaux » : la fin de <c>logs/prospect.log</c> lisible depuis l'application, et un
/// export zip de tous les journaux à joindre à un rapport.
/// </summary>
/// <remarks>
/// <para>
/// Elle existe parce que le journal était jusqu'ici invisible depuis l'application : le seul moyen
/// de le lire était de connaître son chemin et d'ouvrir un explorateur de fichiers, ce qui en fait
/// une pièce que personne ne joint jamais à un rapport. Le défaut d'installation Linux corrigé dans
/// cette même branche a été diagnostiqué à partir d'UNE ligne de ce fichier.
/// </para>
/// <para>
/// La lecture est un INSTANTANÉ, pas un suivi. Rien ne se rafraîchit tout seul : la page le dit
/// franchement et propose un bouton. Un vrai <c>tail -f</c> demanderait un observateur de système
/// de fichiers, une politique de débit et une gestion de la troncature du journal, c'est-à-dire une
/// autre décision que celle-ci.
/// </para>
/// </remarks>
public sealed partial class LogsViewModel : ObservableObject
{
    private readonly AppLogService _logs;
    private readonly IFilePickerService _filePicker;
    private readonly IToastService _toasts;

    /// <summary>Construit la page.</summary>
    /// <param name="logs">Lecture et export du dossier <c>logs/</c>.</param>
    /// <param name="filePicker">Sélecteur de destination de l'export.</param>
    /// <param name="toasts">Pile de toasts, pour rendre compte d'un export abouti.</param>
    public LogsViewModel(AppLogService logs, IFilePickerService filePicker, IToastService toasts)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(toasts);

        _logs = logs;
        _filePicker = filePicker;
        _toasts = toasts;
        LogPathText = logs.AppLogPath;
    }

    /// <summary>Lignes affichées, de la plus ancienne à la plus récente.</summary>
    public ObservableCollection<LogLineViewModel> Lines { get; } = [];

    /// <summary>Chemin du journal d'application, affiché en monospace sous la liste.</summary>
    public string LogPathText { get; }

    /// <summary>Vrai dès qu'il y a quelque chose à lire.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _hasLines;

    /// <summary>Vrai quand aucun journal n'a encore été écrit : la page affiche son état vide.</summary>
    public bool IsEmpty => !HasLines;

    /// <summary>Nombre de fichiers que l'export emporterait, zéro quand il n'y en a aucun.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private int _exportableFileCount;

    /// <summary>Message d'échec de l'export, <see langword="null"/> le reste du temps.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isExporting;

    /// <summary>Exporter n'a de sens qu'avec au moins un journal, et pas deux fois à la fois.</summary>
    public bool CanExport => ExportableFileCount > 0 && !IsExporting;

    /// <summary>Résumé sous le titre : ce qui est affiché et ce qui serait exporté.</summary>
    [ObservableProperty]
    private string _subtitleText = string.Empty;

    /// <summary>Relit la fin du journal. Appelée à l'ouverture de la page et par le bouton.</summary>
    [RelayCommand]
    private void Refresh()
    {
        var entries = _logs.ReadTail();

        Lines.Clear();
        foreach (var entry in entries)
        {
            Lines.Add(new LogLineViewModel(entry));
        }

        HasLines = Lines.Count > 0;
        ExportableFileCount = _logs.FindLogFiles().Count;
        SubtitleText = UiText.Logs.Subtitle(Lines.Count, ExportableFileCount);
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var destination = await _filePicker
            .PickSaveFileAsync(UiText.Logs.ExportPickerTitle, UiText.Logs.ExportFileName, "zip")
            .ConfigureAwait(true);

        if (destination is null)
        {
            // Sélecteur annulé : retour silencieux, ce n'est pas une erreur.
            return;
        }

        ErrorMessage = null;
        IsExporting = true;
        try
        {
            var written = await _logs.ExportAsync(destination, CancellationToken.None).ConfigureAwait(true);
            _toasts.Show(ToastTone.Success, UiText.Toasts.LogsExportedTitle, UiText.Logs.ExportedToastDescription(written));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsExporting = false;
        }
    }
}