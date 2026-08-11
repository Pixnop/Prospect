using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Instances;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Dialogs;

/// <summary>
/// Dialogue de suppression ouvert depuis le menu d'une carte d'instance : la « double
/// confirmation nommant l'instance » voulue par la voix du produit (design/readme.md) est le
/// clic sur « Supprimer » du menu suivi de ce dialogue, dont le titre et le message nomment
/// explicitement l'instance et dont le bouton dit « Supprimer l'instance », jamais « OK ».
/// </summary>
public sealed partial class DeleteInstanceDialogViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly InstanceService _instanceService;
    private readonly IOverlayService _overlay;
    private readonly Func<Task> _requestRefresh;

    public DeleteInstanceDialogViewModel(
        string slug,
        string name,
        InstanceService instanceService,
        IOverlayService overlay,
        Func<Task> requestRefresh)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(requestRefresh);

        _slug = slug;
        _instanceService = instanceService;
        _overlay = overlay;
        _requestRefresh = requestRefresh;
        Title = UiText.Dialogs.DeleteTitle(name);
        Message = UiText.Dialogs.DeleteMessage(name);
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
            await _instanceService.DeleteAsync(_slug).ConfigureAwait(true);
            _overlay.Close();
            await _requestRefresh().ConfigureAwait(true);
        }
        finally
        {
            IsDeleting = false;
        }
    }

    private bool CanConfirm() => !IsDeleting;
}