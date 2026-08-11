using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Instances;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Dialogs;

/// <summary>
/// Dialogue « Renommer » ouvert depuis le menu d'une carte d'instance. Ne change que
/// <see cref="InstanceMetadata.Name"/> via <see cref="InstanceService.RenameAsync"/> : le slug de
/// dossier ne bouge jamais (voir la docstring de cette méthode côté Core).
/// </summary>
public sealed partial class RenameDialogViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly InstanceService _instanceService;
    private readonly IOverlayService _overlay;
    private readonly Func<Task> _requestRefresh;

    public RenameDialogViewModel(
        string slug,
        string currentName,
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
        _name = currentName;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isSaving;

    public string? Error => string.IsNullOrWhiteSpace(Name) ? UiText.Dialogs.RenameEmptyError : null;

    public bool IsValid => Error is null;

    [RelayCommand]
    private void Cancel() => _overlay.Close();

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        IsSaving = true;
        try
        {
            await _instanceService.RenameAsync(_slug, Name.Trim(), CancellationToken.None).ConfigureAwait(true);
            _overlay.Close();
            await _requestRefresh().ConfigureAwait(true);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanConfirm() => IsValid && !IsSaving;
}