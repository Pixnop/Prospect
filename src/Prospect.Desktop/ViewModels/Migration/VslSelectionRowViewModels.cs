using CommunityToolkit.Mvvm.ComponentModel;

using Prospect.Core.Migration;
using Prospect.Desktop.Resources;

namespace Prospect.Desktop.ViewModels.Migration;

/// <summary>
/// Une ligne d'installation détectée dans le flux d'adoption (<see cref="AdoptVslViewModel"/>) :
/// nom, version, taille estimée, mods comptés, case cochée par défaut. <see cref="Source"/> est ce
/// que <see cref="Prospect.Core.Migration.VslAdoptionService.AdoptAsync"/> attend réellement ; le
/// reste n'est que présentation.
/// </summary>
public sealed partial class VslInstallationSelectionRowViewModel : ObservableObject
{
    public VslInstallationSelectionRowViewModel(VslInstallation source, string sizeText, int modCount, bool isSelected)
    {
        ArgumentNullException.ThrowIfNull(source);

        Source = source;
        Name = string.IsNullOrWhiteSpace(source.Name) ? source.Id : source.Name;
        VersionText = string.IsNullOrWhiteSpace(source.Version) ? "version inconnue" : source.Version;
        SizeText = sizeText;
        ModCountText = UiText.Migration.ModCount(modCount);
        _isSelected = isSelected;
    }

    /// <summary>Donnée brute soumise à <see cref="Prospect.Core.Migration.VslAdoptionService.AdoptAsync"/> si cette ligne reste cochée.</summary>
    public VslInstallation Source { get; }

    public string Name { get; }

    public string VersionText { get; }

    public string SizeText { get; }

    public string ModCountText { get; }

    /// <summary>Cochée par défaut à la construction (voir <see cref="AdoptVslViewModel"/> : toutes les installations le sont).</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Une ligne de moteur détecté dans le flux d'adoption : version, taille estimée, case cochée par
/// défaut uniquement si le moteur correspondant manque côté Prospect (voir <see cref="AdoptVslViewModel"/>).
/// </summary>
public sealed partial class VslEngineSelectionRowViewModel : ObservableObject
{
    public VslEngineSelectionRowViewModel(VslGameVersionEntry source, string sizeText, bool isAlreadyInstalled, bool isSelected)
    {
        ArgumentNullException.ThrowIfNull(source);

        Source = source;
        VersionText = source.Version;
        SizeText = sizeText;
        IsAlreadyInstalled = isAlreadyInstalled;
        _isSelected = isSelected;
    }

    /// <summary>Donnée brute soumise à <see cref="Prospect.Core.Migration.VslAdoptionService.AdoptAsync"/> si cette ligne reste cochée.</summary>
    public VslGameVersionEntry Source { get; }

    public string VersionText { get; }

    public string SizeText { get; }

    /// <summary>Vrai si cette version est déjà installée côté Prospect (voir <c>Versions_InstalledBadge</c>).</summary>
    public bool IsAlreadyInstalled { get; }

    [ObservableProperty]
    private bool _isSelected;
}