using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Instances;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Dialogs;

namespace Prospect.Desktop.ViewModels.Home;

/// <summary>
/// Une carte de la grille d'Accueil (design/components/launcher/InstanceCard.jsx). Le bouton
/// « Jouer » reste désactivé pour cette PR (le lancement est une PR ultérieure, voir son tooltip
/// dans la vue) ; les actions du menu ouvrent chacune un dialogue via <see cref="IOverlayService"/>
/// et redemandent un rafraîchissement de l'Accueil après un succès.
/// </summary>
public sealed partial class InstanceCardViewModel : ObservableObject
{
    private const string BuiltinPrefix = "builtin:";
    private const string FallbackIconKey = "layers";

    private readonly InstanceService _instanceService;
    private readonly IOverlayService _overlay;
    private readonly Func<Task> _requestRefresh;

    public InstanceCardViewModel(
        InstanceRecord record,
        string lastPlayedText,
        InstanceService instanceService,
        IOverlayService overlay,
        Func<Task> requestRefresh)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(requestRefresh);

        _instanceService = instanceService;
        _overlay = overlay;
        _requestRefresh = requestRefresh;

        Slug = record.Slug;
        Name = record.Metadata.Name;
        Version = record.Metadata.GameVersion.ToString();
        ChannelBadgeTone = ChannelBadgePresentation.ToBadgeTone(record.Metadata.GameVersion.Channel);
        LastLaunchedUtc = record.Metadata.LastLaunchedUtc;
        LastPlayedText = lastPlayedText;
        IconKey = ParseIconKey(record.Metadata.Icon);
    }

    public string Slug { get; }

    public string Name { get; }

    /// <summary>Valeur machine (voir design/readme.md, « Content fundamentals ») : liée en FontMono côté vue.</summary>
    public string Version { get; }

    /// <summary>Une des quatre teintes de badge du design (voir <see cref="ChannelBadgePresentation"/>).</summary>
    public string ChannelBadgeTone { get; }

    /// <summary>Instant brut, utilisé pour le tri ; <see cref="LastPlayedText"/> porte le texte déjà humanisé.</summary>
    public DateTimeOffset? LastLaunchedUtc { get; }

    public string LastPlayedText { get; }

    /// <summary>Clé résolue par <see cref="Prospect.Desktop.Converters.IconKeyToGeometryConverter"/>.</summary>
    public string IconKey { get; }

    [RelayCommand]
    private void Rename() => _overlay.Show(new RenameDialogViewModel(Slug, Name, _instanceService, _overlay, () => _requestRefresh()));

    [RelayCommand]
    private void Duplicate() => _overlay.Show(new DuplicateDialogViewModel(Slug, Name, _instanceService, _overlay, () => _requestRefresh()));

    [RelayCommand]
    private void Delete() => _overlay.Show(new DeleteInstanceDialogViewModel(Slug, Name, _instanceService, _overlay, () => _requestRefresh()));

    private static string ParseIconKey(string icon)
    {
        if (!icon.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
        {
            return FallbackIconKey;
        }

        var key = icon[BuiltinPrefix.Length..];
        return key is "" or "default" ? FallbackIconKey : key;
    }
}