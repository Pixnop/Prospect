using System.Collections.ObjectModel;

using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.Services;

/// <summary>
/// Pile de toasts de la fenêtre (design/ui_kits/launcher/app-shell.jsx, coin inférieur droit).
/// Service minimal : un ViewModel qui vient de terminer une opération (ex. duplication réussie)
/// y annonce un résultat, sans savoir comment ni où le toast s'affiche.
/// </summary>
public interface IToastService
{
    /// <summary>Toasts actuellement affichés, dans l'ordre d'apparition.</summary>
    ObservableCollection<ToastViewModel> Toasts { get; }

    /// <summary>Affiche un nouveau toast ; il se referme seul après un délai, ou via son bouton de fermeture.</summary>
    void Show(ToastTone tone, string title, string? description = null);
}