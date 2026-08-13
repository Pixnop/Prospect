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
/// La description longue arrive en HTML d'éditeur riche et elle est RENDUE telle que l'auteur l'a
/// écrite : titres, gras, listes imbriquées, code, liens et images. L'analyse est du calcul pur
/// (<see cref="HtmlRichTextParser"/>, dans le Core), la composition en contrôles appartient à la
/// vue (<c>Controls.RichTextPresenter</c>), et ce ViewModel n'est que la charnière. Avant, la
/// fiche aplatissait tout en texte nu : une description structurée y devenait un mur illisible,
/// ce qui a été relevé en test réel (« il faut afficher le html ! »).
///
/// Les captures d'écran de la maquette restent hors périmètre : la description en embarque déjà
/// souvent, et la galerie séparée est un chantier à part pour un bénéfice décoratif.
/// </remarks>
public sealed partial class ModDetailDialogViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Largeur d'affichage des illustrations, en pixels : la colonne de lecture du dialogue
    /// (720 de large, 22 de marge de chaque côté), arrondie sous la barre de défilement.
    /// </summary>
    public const int ReadingColumnWidth = 640;

    private readonly ModDbModDetail _detail;
    private readonly IExternalUrlOpener _urlOpener;
    private readonly IOverlayService _overlay;
    private readonly Func<Task> _install;

    /// <summary>Construit la fiche.</summary>
    /// <param name="detail">Fiche complète telle que rendue par l'API.</param>
    /// <param name="target">Instance cible choisie dans le navigateur, ou <see langword="null"/>.</param>
    /// <param name="urlOpener">Ouverture de la page officielle et des liens de la description.</param>
    /// <param name="overlay">Panneau modal, pour se refermer.</param>
    /// <param name="images">Cache d'images, partagé avec les vignettes du navigateur.</param>
    /// <param name="install">Lance la préparation d'installation.</param>
    public ModDetailDialogViewModel(
        ModDbModDetail detail,
        ModTargetInstanceViewModel? target,
        IExternalUrlOpener urlOpener,
        IOverlayService overlay,
        IModLogoCache images,
        Func<Task> install)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(urlOpener);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(install);

        _detail = detail;
        _urlOpener = urlOpener;
        _overlay = overlay;
        _install = install;

        Name = HtmlText.DecodeEntities(detail.Name);
        MetaText = UiText.Mods.DetailMeta(detail.Author, detail.Downloads);
        Description = new RichTextDocumentViewModel(
            HtmlRichTextParser.Parse(detail.DescriptionHtml),
            urlOpener,
            images,
            ReadingColumnWidth);
        HasDescription = !Description.IsEmpty;
        PageUrlText = detail.PageUrl.ToString();

        Logo = detail.LogoUrl is null
            ? null
            : new RichTextImageViewModel(new RichTextImage(detail.LogoUrl, detail.Name, Link: null), images, LogoWidth);

        // Ce que l'API porte déjà et que la fiche gardait pour elle : les adresses déclarées par
        // l'auteur, ouvertes par le même IExternalUrlOpener que la page ModDB. Une adresse vide ou
        // relative est écartée par le mapper, jamais devinée (voir ModDbMapper.ToExternalUri).
        Links =
        [
            .. new (Uri? Url, string Label)[]
            {
                (detail.SourceCodeUrl, UiText.Mods.LinkSourceCode),
                (detail.IssueTrackerUrl, UiText.Mods.LinkIssues),
                (detail.WikiUrl, UiText.Mods.LinkWiki),
                (detail.HomepageUrl, UiText.Mods.LinkHomepage),
            }
                .Where(entry => entry.Url is not null)
                .Select(entry => new ModExternalLinkViewModel(entry.Label, entry.Url!, urlOpener)),
        ];
        HasLinks = Links.Count > 0;

        Releases = detail.Releases
            .Take(MaxReleasesShown)
            .Select(release => new ModReleaseRowViewModel(release, target?.GameVersion))
            .ToArray();
        HasReleases = Releases.Count > 0;
        CanInstall = target?.Slug is not null;
    }

    /// <summary>Nombre de releases listées : au-delà, la fiche officielle fait mieux que nous.</summary>
    public const int MaxReleasesShown = 8;

    /// <summary>Largeur d'affichage du logo d'en-tête, en pixels.</summary>
    private const int LogoWidth = 128;

    public string Name { get; }

    public string MetaText { get; }

    /// <summary>Description ModDB rendue telle que publiée, blocs et liens compris.</summary>
    public RichTextDocumentViewModel Description { get; }

    public bool HasDescription { get; }

    /// <summary>Logo de la fiche, ou <see langword="null"/> quand elle n'en a pas.</summary>
    public RichTextImageViewModel? Logo { get; }

    /// <summary>URL de la fiche officielle, affichée en monospace sous le bouton.</summary>
    public string PageUrlText { get; }

    /// <summary>Adresses externes déclarées par l'auteur (code source, tickets, wiki, site).</summary>
    public IReadOnlyList<ModExternalLinkViewModel> Links { get; }

    public bool HasLinks { get; }

    public IReadOnlyList<ModReleaseRowViewModel> Releases { get; }

    public bool HasReleases { get; }

    /// <summary>Faux quand aucune instance cible n'est choisie : il n'y a alors nulle part où installer.</summary>
    public bool CanInstall { get; }

    [ObservableProperty]
    private bool _openFailed;

    /// <summary>Annule les chargements d'images encore en vol quand la fiche se referme.</summary>
    public void Dispose()
    {
        Description.Dispose();
        Logo?.Dispose();
    }

    [RelayCommand]
    private void Close()
    {
        _overlay.Close();
        Dispose();
    }

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

/// <summary>Un lien externe déclaré sur la fiche, ouvert dans le navigateur du système.</summary>
public sealed partial class ModExternalLinkViewModel : ObservableObject
{
    private readonly IExternalUrlOpener _urlOpener;

    /// <summary>Construit le lien.</summary>
    /// <param name="label">Libellé traduit.</param>
    /// <param name="url">Adresse absolue.</param>
    /// <param name="urlOpener">Ouverture système.</param>
    public ModExternalLinkViewModel(string label, Uri url, IExternalUrlOpener urlOpener)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(urlOpener);

        Label = label;
        Url = url;
        _urlOpener = urlOpener;
    }

    /// <summary>Libellé du bouton.</summary>
    public string Label { get; }

    /// <summary>Adresse ouverte au clic.</summary>
    public Uri Url { get; }

    [RelayCommand]
    private Task<bool> OpenAsync() => _urlOpener.OpenAsync(Url, CancellationToken.None);
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

        // Le badge porte le VERDICT (la version de jeu visée), pas la liste des versions
        // compatibles. Sur les fiches réelles, cette liste monte à 23 versions pour une seule
        // release (docs/research/moddb-api.md, et vérifié par l'étage live sur configlib) : un
        // badge de cette largeur sortait de la boîte du dialogue. Même sémantique que le badge
        // d'une carte du navigateur, qui affiche déjà la version visée et rien d'autre.
        BadgeText = targetGameVersion?.ToString() ?? string.Empty;
        BadgeTone = targetGameVersion is null ? string.Empty : isCompatible ? "stable" : "incompatible";
        ShowBadge = targetGameVersion is not null;
    }

    public string VersionText { get; }

    /// <summary>Versions de jeu déclarées compatibles, déjà résumées quand elles sont nombreuses.</summary>
    public string GameVersionsText { get; }

    public string DateText { get; }

    /// <summary>Version de jeu de l'instance cible, ou chaîne vide quand il n'y a rien à affirmer.</summary>
    public string BadgeText { get; }

    public string BadgeTone { get; }

    public bool ShowBadge { get; }
}