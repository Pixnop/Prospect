using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Dialogs;

/// <summary>
/// Confirmation avant de désinstaller une version du jeu. Quand des instances la référencent,
/// l'avertissement les nomme : c'est la différence entre une information sur laquelle
/// l'utilisateur peut décider et un message générique qu'il ne peut que subir.
/// </summary>
public sealed partial class UninstallVersionDialogViewModel : ObservableObject
{
    private readonly Func<Task> _onConfirm;
    private readonly IOverlayService _overlay;

    public UninstallVersionDialogViewModel(
        string versionText,
        IReadOnlyList<string> dependentInstanceNames,
        Func<Task> onConfirm,
        IOverlayService overlay)
    {
        ArgumentNullException.ThrowIfNull(dependentInstanceNames);
        ArgumentNullException.ThrowIfNull(onConfirm);
        ArgumentNullException.ThrowIfNull(overlay);

        _onConfirm = onConfirm;
        _overlay = overlay;
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
    private bool _isBusy;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        IsBusy = true;
        try
        {
            await _onConfirm().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConfirm() => !IsBusy;

    [RelayCommand]
    private void Cancel() => _overlay.Close();
}