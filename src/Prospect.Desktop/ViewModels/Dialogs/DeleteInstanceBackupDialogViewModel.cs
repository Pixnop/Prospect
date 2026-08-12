using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Backups;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Dialogs;

/// <summary>
/// Dialogue « Supprimer » ouvert depuis une ligne de sauvegarde du bloc Sauvegardes (onglet
/// Options) : confirmation nommant la date de la sauvegarde concernée, même schéma que
/// <c>UninstallModDialogViewModel</c>.
/// </summary>
public sealed partial class DeleteInstanceBackupDialogViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly string _fileName;
    private readonly InstanceBackupService _backupService;
    private readonly IOverlayService _overlay;
    private readonly Func<Task> _onDeleted;

    public DeleteInstanceBackupDialogViewModel(
        string slug,
        string fileName,
        string dateText,
        InstanceBackupService backupService,
        IOverlayService overlay,
        Func<Task> onDeleted)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(backupService);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(onDeleted);

        _slug = slug;
        _fileName = fileName;
        _backupService = backupService;
        _overlay = overlay;
        _onDeleted = onDeleted;

        Title = UiText.Dialogs.DeleteBackupTitle(dateText);
        Message = UiText.Dialogs.DeleteBackupMessage;
    }

    public string Title { get; }

    public string Message { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isDeleting;

    [RelayCommand]
    private void Cancel() => _overlay.Close();

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        IsDeleting = true;
        try
        {
            await _backupService.DeleteAsync(_slug, _fileName, CancellationToken.None).ConfigureAwait(true);
            _overlay.Close();
            await _onDeleted().ConfigureAwait(true);
        }
        catch (InstanceBackupNotFoundException)
        {
            // Déjà supprimée par ailleurs (double-clic, autre fenêtre) : fermer quand même, le
            // rafraîchissement suivant reflétera l'absence.
            _overlay.Close();
            await _onDeleted().ConfigureAwait(true);
        }
        finally
        {
            IsDeleting = false;
        }
    }

    private bool CanConfirm() => !IsDeleting;
}