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
public sealed partial class UninstallModDialogViewModel : IDisposable
{
    private readonly Func<Task> _confirm;
    private readonly IOverlayService _overlay;

    /// <summary>Construit le dialogue.</summary>
    /// <param name="impact">Résultat de la vérification inverse.</param>
    /// <param name="confirm">Action de désinstallation.</param>
    /// <param name="overlay">Panneau modal, pour se refermer.</param>
    /// <param name="logoDirectory">Annuaire des logos, ou <see langword="null"/> pour un dialogue sans vignette.</param>
    /// <param name="images">Cache d'images, ou <see langword="null"/> pour un dialogue sans vignette.</param>
    public UninstallModDialogViewModel(
        ModUninstallImpact impact,
        Func<Task> confirm,
        IOverlayService overlay,
        IModLogoDirectory? logoDirectory = null,
        IModLogoCache? images = null)
    {
        ArgumentNullException.ThrowIfNull(impact);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(overlay);

        _confirm = confirm;
        _overlay = overlay;

        // Même règle que la rangée qui a ouvert ce dialogue : la vignette vient de la PROVENANCE,
        // donc un mod déposé à la main garde le pictogramme. Retirer un mod est une décision, et
        // ce dialogue doit montrer la même chose que la rangée d'où on vient — pas mieux.
        Thumbnail = logoDirectory is null || images is null
            ? ModThumbnailViewModel.None
            : new ModThumbnailViewModel(impact.Target.Provenance?.ModId, logoDirectory, images);

        Title = UiText.Mods.UninstallTitle(impact.Target.DisplayName);
        Message = UiText.Mods.UninstallMessage(impact.Target.FileName);
        HasDependents = impact.HasDependents;
        DependentsMessage = UiText.Mods.UninstallDependents(impact.DependentNames);
    }

    /// <summary>Vignette du mod à retirer, ou le pictogramme générique.</summary>
    public ModThumbnailViewModel Thumbnail { get; }

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

    /// <summary>Annule le chargement de vignette encore en vol (l'overlay dispose ce qu'il ferme).</summary>
    public void Dispose() => Thumbnail.Dispose();
}