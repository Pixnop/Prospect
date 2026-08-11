using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.ModDb;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Mods;

/// <summary>
/// Fiche d'un mod, affichée en dialog par-dessus le navigateur
/// (design/ui_kits/launcher/screen-mods.jsx, composant <c>ModDetail</c>).
/// </summary>
/// <remarks>
/// Deux écarts assumés par rapport à la maquette, tous deux dictés par ce que l'API livre
/// réellement. La description longue arrive en HTML brut d'éditeur WYSIWYG : elle est affichée
/// NETTOYÉE (<see cref="HtmlText.ToPlainText"/>) plutôt que rendue, et le bouton « Ouvrir sur le
/// ModDB » renvoie à la page officielle pour la version complète. Et les captures d'écran de la
/// maquette ne sont pas affichées en MVP : ce sont des URLs distantes à télécharger et à mettre en
/// cache, un chantier à part entière pour un bénéfice décoratif.
/// </remarks>
public sealed partial class ModDetailDialogViewModel : ObservableObject
{
    private readonly ModDbModDetail _detail;
    private readonly IExternalUrlOpener _urlOpener;
    private readonly IOverlayService _overlay;
    private readonly Func<Task> _install;

    /// <summary>Construit la fiche.</summary>
    /// <param name="detail">Fiche complète telle que rendue par l'API.</param>
    /// <param name="target">Instance cible choisie dans le navigateur, ou <see langword="null"/>.</param>
    /// <param name="urlOpener">Ouverture de la page officielle.</param>
    /// <param name="overlay">Panneau modal, pour se refermer.</param>
    /// <param name="install">Lance la préparation d'installation.</param>
    public ModDetailDialogViewModel(
        ModDbModDetail detail,
        ModTargetInstanceViewModel? target,
        IExternalUrlOpener urlOpener,
        IOverlayService overlay,
        Func<Task> install)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(urlOpener);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(install);

        _detail = detail;
        _urlOpener = urlOpener;
        _overlay = overlay;
        _install = install;

        Name = HtmlText.DecodeEntities(detail.Name);
        MetaText = UiText.Mods.DetailMeta(detail.Author, detail.Downloads);
        Description = HtmlText.ToPlainText(detail.DescriptionHtml);
        HasDescription = Description.Length > 0;
        PageUrlText = detail.PageUrl.ToString();

        Releases = detail.Releases
            .Take(MaxReleasesShown)
            .Select(release => new ModReleaseRowViewModel(release, target?.GameVersion))
            .ToArray();
        HasReleases = Releases.Count > 0;
        CanInstall = target?.Slug is not null;
    }

    /// <summary>Nombre de releases listées : au-delà, la fiche officielle fait mieux que nous.</summary>
    public const int MaxReleasesShown = 8;

    public string Name { get; }

    public string MetaText { get; }

    /// <summary>Description ModDB nettoyée de son balisage.</summary>
    public string Description { get; }

    public bool HasDescription { get; }

    /// <summary>URL de la fiche officielle, affichée en monospace sous le bouton.</summary>
    public string PageUrlText { get; }

    public IReadOnlyList<ModReleaseRowViewModel> Releases { get; }

    public bool HasReleases { get; }

    /// <summary>Faux quand aucune instance cible n'est choisie : il n'y a alors nulle part où installer.</summary>
    public bool CanInstall { get; }

    [ObservableProperty]
    private bool _openFailed;

    [RelayCommand]
    private void Close() => _overlay.Close();

    [RelayCommand]
    private async Task OpenOnModDbAsync()
        => OpenFailed = !await _urlOpener.OpenAsync(_detail.PageUrl, CancellationToken.None).ConfigureAwait(true);

    [RelayCommand]
    private async Task InstallAsync()
    {
        _overlay.Close();
        await _install().ConfigureAwait(true);
    }
}

/// <summary>Une ligne de l'historique des versions, dans la fiche d'un mod.</summary>
public sealed class ModReleaseRowViewModel
{
    /// <summary>Construit la ligne.</summary>
    /// <param name="release">Release publiée.</param>
    /// <param name="targetGameVersion">Version de jeu de l'instance cible, ou <see langword="null"/>.</param>
    public ModReleaseRowViewModel(ModDbRelease release, GameVersion? targetGameVersion)
    {
        ArgumentNullException.ThrowIfNull(release);

        VersionText = release.Version.ToString();
        GameVersionsText = UiText.Mods.CompatibleVersions(release.CompatibleGameVersionTags);
        DateText = UiText.Mods.ReleaseDate(release.CreatedUtc);

        var isCompatible = targetGameVersion is { } version && release.CompatibleGameVersions.Contains(version);
        BadgeTone = targetGameVersion is null ? string.Empty : isCompatible ? "stable" : "incompatible";
        ShowBadge = targetGameVersion is not null;
    }

    public string VersionText { get; }

    public string GameVersionsText { get; }

    public string DateText { get; }

    public string BadgeTone { get; }

    public bool ShowBadge { get; }
}