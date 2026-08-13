using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Dialogs;

/// <summary>
/// Confirmation avant de désinstaller une version du jeu. Quand des instances la référencent,
/// l'avertissement les nomme : c'est la différence entre une information sur laquelle
/// l'utilisateur peut décider et un message générique qu'il ne peut que subir.
/// </summary>
/// <remarks>
/// Le dialogue RESTE ouvert pendant la suppression, et il compte. Effacer une installation de six
/// cents mégaoctets prend des dizaines de secondes ; le travail est déporté hors du thread
/// d'interface (voir <c>GameInstallService.UninstallAsync</c>), donc la fenêtre continue de se
/// dessiner, et autant qu'elle dise où elle en est. Aucune annulation n'est offerte une fois la
/// suppression commencée : une version à moitié supprimée est pire que les deux états francs.
/// </remarks>
public sealed partial class UninstallVersionDialogViewModel : ObservableObject, IProgress<DirectoryDeleteProgress>
{
    private readonly Func<IProgress<DirectoryDeleteProgress>, Task> _onConfirm;
    private readonly IOverlayService _overlay;
    private readonly IUiDispatcher _dispatcher;

    public UninstallVersionDialogViewModel(
        string versionText,
        IReadOnlyList<string> dependentInstanceNames,
        Func<IProgress<DirectoryDeleteProgress>, Task> onConfirm,
        IOverlayService overlay,
        IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dependentInstanceNames);
        ArgumentNullException.ThrowIfNull(onConfirm);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _onConfirm = onConfirm;
        _overlay = overlay;
        _dispatcher = dispatcher;
        VersionText = versionText;
        Title = UiText.Versions.UninstallTitle(versionText);
        Message = UiText.Versions.UninstallMessage(versionText);
        DependentsMessage = dependentInstanceNames.Count == 0
            ? null
            : UiText.Versions.UninstallDependents(dependentInstanceNames);
        HasDependents = DependentsMessage is not null;
    }

    public string VersionText { get; }

    public string Title { get; }

    public string Message { get; }

    /// <summary>Phrase nommant les instances concernées, <see langword="null"/> quand aucune ne l'est.</summary>
    public string? DependentsMessage { get; }

    public bool HasDependents { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    /// <summary>Avancement de la suppression, de 0 à 100.</summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>« Suppression des fichiers (128/540) », ou la phrase d'attente avant le premier relevé.</summary>
    [ObservableProperty]
    private string _progressText = UiText.Versions.UninstallInProgress;

    /// <summary>Message d'échec, quand il reste des fichiers sur le disque. Le dialogue reste ouvert.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <inheritdoc />
    public void Report(DirectoryDeleteProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _dispatcher.Post(() =>
        {
            ProgressPercent = value.Ratio * 100d;
            ProgressText = UiText.Versions.UninstallProgress(value.DeletedFiles, value.TotalFiles);
        });
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        ProgressPercent = 0d;
        ProgressText = UiText.Versions.UninstallInProgress;

        try
        {
            await _onConfirm(this).ConfigureAwait(true);
        }
        catch (DirectoryDeleteFailedException exception)
        {
            // Message honnête plutôt que silence : il reste quelque chose, et l'écran doit quand
            // même être rafraîchi puisque la suppression a pu emporter une partie du dossier.
            ErrorMessage = UiText.Versions.UninstallPartialFailure(exception.Directory);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConfirm() => !IsBusy;

    // Le bouton disparaît plutôt que de mentir : rien n'est annulable une fois commencé.
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _overlay.Close();

    private bool CanCancel() => !IsBusy;
}