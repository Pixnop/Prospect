using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Dialogs;

/// <summary>
/// Confirmation avant d'arrêter une session de jeu en cours (voix du produit : le message nomme
/// l'instance et prévient explicitement de la perte de progression non sauvegardée, même esprit
/// que <see cref="DeleteInstanceDialogViewModel"/>). Contrairement aux autres dialogues, la
/// confirmation ne fait pas d'appel asynchrone : <c>RunningInstanceTracker.RequestStop</c> ne fait
/// que demander la fin du processus (kill immédiat, pas d'attente), donc pas d'état « en cours ».
/// </summary>
public sealed partial class StopInstanceDialogViewModel : ObservableObject
{
    private readonly Action _onConfirm;
    private readonly IOverlayService _overlay;

    public StopInstanceDialogViewModel(string instanceName, Action onConfirm, IOverlayService overlay)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceName);
        ArgumentNullException.ThrowIfNull(onConfirm);
        ArgumentNullException.ThrowIfNull(overlay);

        _onConfirm = onConfirm;
        _overlay = overlay;
        Title = UiText.Instance.StopConfirmTitle(instanceName);
        Message = UiText.Instance.StopConfirmMessage(instanceName);
    }

    public string Title { get; }

    public string Message { get; }

    [RelayCommand]
    private void Cancel() => _overlay.Close();

    [RelayCommand]
    private void Confirm()
    {
        _onConfirm();
        _overlay.Close();
    }
}