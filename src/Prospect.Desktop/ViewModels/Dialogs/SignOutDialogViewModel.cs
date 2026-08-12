using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Dialogs;

/// <summary>
/// Confirmation avant de déconnecter le compte Vintage Story. Même gabarit que
/// <see cref="StopInstanceDialogViewModel"/> : le titre nomme le joueur, le message dit ce qui
/// change vraiment (le jeu redémarrera sans session multijoueur), et le bouton dit « Se
/// déconnecter », jamais « OK ».
/// </summary>
/// <remarks>
/// La confirmation est asynchrone, contrairement à l'arrêt d'une instance : la déconnexion efface
/// un fichier, donc touche au disque. Le dialogue se ferme après, pas avant, pour que rien ne
/// laisse croire que c'est fait tant que ça ne l'est pas.
/// </remarks>
public sealed partial class SignOutDialogViewModel : ObservableObject
{
    private readonly Func<Task> _onConfirm;
    private readonly IOverlayService _overlay;

    public SignOutDialogViewModel(string playerName, Func<Task> onConfirm, IOverlayService overlay)
    {
        ArgumentException.ThrowIfNullOrEmpty(playerName);
        ArgumentNullException.ThrowIfNull(onConfirm);
        ArgumentNullException.ThrowIfNull(overlay);

        _onConfirm = onConfirm;
        _overlay = overlay;
        Title = UiText.Account.SignOutConfirmTitle(playerName);
        Message = UiText.Account.SignOutConfirmMessage;
    }

    public string Title { get; }

    public string Message { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isSigningOut;

    [RelayCommand]
    private void Cancel() => _overlay.Close();

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        IsSigningOut = true;
        try
        {
            await _onConfirm().ConfigureAwait(true);
            _overlay.Close();
        }
        finally
        {
            IsSigningOut = false;
        }
    }

    private bool CanConfirm() => !IsSigningOut;
}