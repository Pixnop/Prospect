using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Backups;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Dialogs;

/// <summary>
/// Dialogue « Restaurer » ouvert depuis une ligne de sauvegarde du bloc Sauvegardes (onglet
/// Options) : la double confirmation voulue par la mission est le clic sur « Restaurer » de la
/// ligne suivi de CE dialogue, dont le titre et le message nomment explicitement l'instance et la
/// date de la sauvegarde, et mentionnent la sauvegarde de sécurité automatique que
/// <see cref="InstanceBackupService.RestoreAsync"/> prend de l'état courant avant d'écraser quoi
/// que ce soit (même schéma que <c>DeleteInstanceDialogViewModel</c>, appliqué ici à une action
/// plus grave encore : celle-ci ne supprime pas, elle REMPLACE tout <c>data/</c>).
/// </summary>
public sealed partial class RestoreInstanceBackupDialogViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly string _fileName;
    private readonly InstanceBackupService _backupService;
    private readonly IOverlayService _overlay;
    private readonly Func<Task> _onRestored;

    public RestoreInstanceBackupDialogViewModel(
        string slug,
        string instanceName,
        string fileName,
        string dateText,
        InstanceBackupService backupService,
        IOverlayService overlay,
        Func<Task> onRestored)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(backupService);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(onRestored);

        _slug = slug;
        _fileName = fileName;
        _backupService = backupService;
        _overlay = overlay;
        _onRestored = onRestored;

        Title = UiText.Dialogs.RestoreBackupTitle(instanceName);
        Message = UiText.Dialogs.RestoreBackupMessage(instanceName, dateText);
    }

    public string Title { get; }

    public string Message { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isRestoring;

    [ObservableProperty]
    private string? _failureMessage;

    [RelayCommand]
    private void Cancel() => _overlay.Close();

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        FailureMessage = null;
        IsRestoring = true;
        try
        {
            await _backupService.RestoreAsync(_slug, _fileName, progress: null, CancellationToken.None).ConfigureAwait(true);
            _overlay.Close();
            await _onRestored().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InstanceBackupNotFoundException or IOException or UnauthorizedAccessException)
        {
            FailureMessage = exception.Message;
        }
        finally
        {
            IsRestoring = false;
        }
    }

    private bool CanConfirm() => !IsRestoring;
}