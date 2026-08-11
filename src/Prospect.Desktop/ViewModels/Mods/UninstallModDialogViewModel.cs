using CommunityToolkit.Mvvm.Input;

using Prospect.Core.ModDb;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Mods;

/// <summary>
/// Confirmation de désinstallation d'un mod. Quand d'autres mods installés le déclarent en
/// dépendance, ils sont NOMMÉS dans un encadré d'avertissement plutôt que résumés en « des mods en
/// dépendent » : l'utilisateur doit pouvoir décider sur ce qu'il lit, exactement comme pour la
/// désinstallation d'une version du jeu.
/// </summary>
public sealed partial class UninstallModDialogViewModel
{
    private readonly Func<Task> _confirm;
    private readonly IOverlayService _overlay;

    /// <summary>Construit le dialogue.</summary>
    /// <param name="impact">Résultat de la vérification inverse.</param>
    /// <param name="confirm">Action de désinstallation.</param>
    /// <param name="overlay">Panneau modal, pour se refermer.</param>
    public UninstallModDialogViewModel(ModUninstallImpact impact, Func<Task> confirm, IOverlayService overlay)
    {
        ArgumentNullException.ThrowIfNull(impact);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(overlay);

        _confirm = confirm;
        _overlay = overlay;

        Title = UiText.Mods.UninstallTitle(impact.Target.DisplayName);
        Message = UiText.Mods.UninstallMessage(impact.Target.FileName);
        HasDependents = impact.HasDependents;
        DependentsMessage = UiText.Mods.UninstallDependents(impact.DependentNames);
    }

    public string Title { get; }

    public string Message { get; }

    /// <summary>Vrai si au moins un mod installé déclare celui-ci en dépendance.</summary>
    public bool HasDependents { get; }

    /// <summary>Phrase qui nomme les mods concernés, vide s'il n'y en a aucun.</summary>
    public string DependentsMessage { get; }

    [RelayCommand]
    private void Cancel() => _overlay.Close();

    [RelayCommand]
    private Task Confirm() => _confirm();
}