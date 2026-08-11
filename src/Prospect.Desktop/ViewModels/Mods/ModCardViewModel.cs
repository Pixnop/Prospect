using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.ModDb;
using Prospect.Desktop.Resources;

namespace Prospect.Desktop.ViewModels.Mods;

/// <summary>
/// Une carte de résultat du navigateur de mods (design/ui_kits/launcher/screen-mods.jsx,
/// composant <c>ModCard</c>) : nom, auteur, téléchargements, résumé, badges de compatibilité et de
/// côté, bouton Installer.
/// </summary>
public sealed partial class ModCardViewModel : ObservableObject
{
    private readonly Func<ModCardViewModel, Task> _open;
    private readonly Func<ModCardViewModel, Task> _install;

    /// <summary>Construit la carte.</summary>
    /// <param name="summary">Entrée de catalogue.</param>
    /// <param name="compatibility">État de compatibilité vis-à-vis de l'instance cible.</param>
    /// <param name="open">Ouvre la fiche détaillée.</param>
    /// <param name="install">Lance la préparation d'installation.</param>
    public ModCardViewModel(
        ModDbModSummary summary,
        ModCompatibilityBadge compatibility,
        Func<ModCardViewModel, Task> open,
        Func<ModCardViewModel, Task> install)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(install);

        Summary = summary;
        _open = open;
        _install = install;

        Name = HtmlText.DecodeEntities(summary.Name);
        AuthorText = UiText.Mods.ByAuthor(summary.Author);
        DownloadsText = UiText.Mods.FormatCount(summary.Downloads);
        Description = HtmlText.DecodeEntities(summary.Summary);
        SideText = UiText.Mods.SideLabel(summary.Side);
        HasSide = summary.Side != ModDbSide.Unknown;
        _compatibility = compatibility;
    }

    /// <summary>Entrée de catalogue derrière cette carte.</summary>
    public ModDbModSummary Summary { get; }

    /// <summary>Identifiant numérique de la fiche.</summary>
    public int ModId => Summary.ModId;

    public string Name { get; }

    public string AuthorText { get; }

    public string DownloadsText { get; }

    public string Description { get; }

    public string SideText { get; }

    public bool HasSide { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompatibilityText))]
    [NotifyPropertyChangedFor(nameof(CompatibilityTone))]
    [NotifyPropertyChangedFor(nameof(ShowCompatibility))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private ModCompatibilityBadge _compatibility;

    /// <summary>Libellé du badge de version, vide quand aucune instance cible n'est choisie.</summary>
    public string CompatibilityText => Compatibility.Text;

    /// <summary>Ton du badge, aligné sur le vocabulaire de canaux du design.</summary>
    public string CompatibilityTone => Compatibility.Tone;

    public bool ShowCompatibility => Compatibility.IsVisible;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private Task OpenAsync() => _open(this);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync() => _install(this);

    private bool CanInstall() => !IsBusy;

    partial void OnIsBusyChanged(bool value) => InstallCommand.NotifyCanExecuteChanged();
}

/// <summary>
/// Ce qu'affiche le badge de compatibilité d'une carte. Trois états seulement, parce que l'API ne
/// permet pas d'en distinguer davantage sans ouvrir la fiche de chaque mod : compatible,
/// incompatible, ou pas d'avis (aucune instance cible choisie, ou index de compatibilité
/// indisponible parce que le ModDB n'a pas répondu).
/// </summary>
/// <param name="Text">Libellé, généralement la version de jeu visée.</param>
/// <param name="Tone">Classe de ton du badge : <c>stable</c>, <c>incompatible</c> ou vide.</param>
/// <param name="IsVisible">Faux quand il n'y a rien à affirmer.</param>
/// <param name="IsCompatible">Vrai si le mod a une release taguée pour la version visée.</param>
public sealed record ModCompatibilityBadge(string Text, string Tone, bool IsVisible, bool IsCompatible)
{
    /// <summary>Aucun avis : pas d'instance cible, ou index indisponible.</summary>
    public static ModCompatibilityBadge None { get; } = new(string.Empty, string.Empty, IsVisible: false, IsCompatible: true);

    /// <summary>Construit le badge pour une version de jeu et un verdict.</summary>
    public static ModCompatibilityBadge For(string gameVersionText, bool isCompatible)
        => new(gameVersionText, isCompatible ? "stable" : "incompatible", IsVisible: true, isCompatible);
}