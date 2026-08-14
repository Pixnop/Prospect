using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.ModDb;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Mods;

/// <summary>
/// Confirmation d'installation : la version à poser, ses notes de version, et la liste COCHABLE
/// des dépendances déclarées manquantes.
/// </summary>
/// <remarks>
/// <para>
/// Ce dialogue est le seul chemin par lequel une dépendance peut être installée. Les cases sont
/// cochées d'avance parce que c'est presque toujours ce qu'on veut, mais elles restent décochables
/// et rien ne part sans que ce bouton soit cliqué : le contrat dit « proposées à l'installation en
/// un clic, jamais installées en silence », et une case cochée que l'utilisateur voit et peut
/// décocher n'est pas du silence.
/// </para>
/// <para>
/// Le SÉLECTEUR DE VERSION, lui, n'ajoute aucune mécanique : changer de release rejoue
/// <see cref="ModInstallService.PrepareAsync"/> avec l'identifiant choisi, et le plan qui en sort
/// remplace celui d'avant. C'est indispensable et pas seulement propre, parce que les dépendances
/// d'un mod se lisent dans le <c>modinfo.json</c> de l'archive TÉLÉCHARGÉE : une release plus
/// ancienne n'a pas forcément les mêmes, et une liste de dépendances laissée telle quelle après un
/// changement de version serait tout simplement fausse.
/// </para>
/// </remarks>
public sealed partial class ModInstallPlanDialogViewModel : ObservableObject, IDisposable
{
    private readonly Func<int, Task<ModInstallPlan>> _reprepare;
    private readonly Func<ModInstallPlan, IReadOnlyCollection<string>, Task> _confirm;
    private readonly IOverlayService _overlay;
    private readonly IModLogoCache _images;
    private readonly IModLogoDirectory _logoDirectory;
    private readonly string _instanceName;

    private bool _isApplyingPlan;

    /// <summary>Construit le dialogue.</summary>
    /// <param name="plan">Plan produit par <see cref="ModInstallService.PrepareAsync"/>.</param>
    /// <param name="instanceName">Nom de l'instance cible, montré dans le titre.</param>
    /// <param name="confirm">Applique le plan COURANT avec les dépendances retenues.</param>
    /// <param name="reprepare">Recalcule le plan pour une autre release.</param>
    /// <param name="overlay">Panneau modal, pour se refermer.</param>
    /// <param name="urlOpener">Ouverture des liens présents dans un changelog.</param>
    /// <param name="images">Cache d'images, pour les illustrations d'un changelog et pour les vignettes.</param>
    /// <param name="logoDirectory">Annuaire des logos, pour la vignette du mod et celles de ses dépendances.</param>
    public ModInstallPlanDialogViewModel(
        ModInstallPlan plan,
        string instanceName,
        Func<ModInstallPlan, IReadOnlyCollection<string>, Task> confirm,
        Func<int, Task<ModInstallPlan>> reprepare,
        IOverlayService overlay,
        IExternalUrlOpener urlOpener,
        IModLogoCache images,
        IModLogoDirectory logoDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(reprepare);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(urlOpener);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(logoDirectory);

        _confirm = confirm;
        _reprepare = reprepare;
        _overlay = overlay;
        _instanceName = instanceName;
        _images = images;
        _logoDirectory = logoDirectory;

        // La vignette de l'en-tête est construite UNE FOIS : changer de release dans le sélecteur
        // recalcule tout le reste du dialogue, mais pas la fiche, donc pas son logo. La rebâtir à
        // chaque recalcul redemanderait la même image au cache pour rien.
        Thumbnail = new ModThumbnailViewModel(plan.Primary.ModDbModId, logoDirectory, images);

        _plan = plan;
        _allReleases = plan.AvailableReleases
            .Select(choice => new ModReleaseChoiceViewModel(choice, urlOpener, images))
            .ToArray();

        // Le décompte annoncé porte sur les COMPATIBLES : c'est le nombre de versions que Prospect
        // se sent d'installer sans avertir, pas le nombre de fichiers publiés.
        _compatibleReleases = [.. _allReleases.Where(release => !release.IsDeclaredIncompatible)];
        ReleaseCountText = UiText.Mods.ReleaseChoiceCount(_compatibleReleases.Length);
        HasIncompatibleReleases = _allReleases.Length > _compatibleReleases.Length;

        // Le dévoilement est POSÉ D'EMBLÉE quand il n'y a rien d'autre à voir : une fiche dont
        // aucune release n'est déclarée compatible ouvrirait sinon sur un sélecteur vide, c'est-à-dire
        // sur un dialogue qui montre un mod, un bouton Installer, et pas une seule version à
        // installer. Le contrat « pas d'élargissement silencieux » est respecté à la lettre, parce
        // que rien n'est silencieux ici : l'avertissement de non-compatibilité est affiché avec la
        // liste, et c'est bien ce que le bouton de dévoilement aurait montré au premier clic. On
        // épargne ce clic, on n'épargne pas l'information.
        _showsAllReleases = _compatibleReleases.Length == 0 && _allReleases.Length > 0;
        _releases = _showsAllReleases ? _allReleases : _compatibleReleases;

        // ApplyPlan présélectionne la release du plan, c'est-à-dire la meilleure que le domaine ait
        // su retenir : la plus récente compatible quand il y en a une, la meilleure publiée sinon.
        ApplyPlan(plan);
    }

    /// <summary>Vignette du mod à installer, ou le pictogramme générique. Ne change jamais de fiche.</summary>
    public ModThumbnailViewModel Thumbnail { get; }

    /// <summary>Plan courant : celui du dernier calcul, pas forcément celui de la construction.</summary>
    [ObservableProperty]
    private ModInstallPlan _plan;

    private readonly ModReleaseChoiceViewModel[] _allReleases;
    private readonly ModReleaseChoiceViewModel[] _compatibleReleases;

    /// <summary>
    /// Releases actuellement proposées, la plus récente d'abord : les compatibles seules tant que
    /// le dévoilement n'a pas été demandé.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<ModReleaseChoiceViewModel> _releases;

    /// <summary>Décompte des versions COMPATIBLES, affiché au-dessus du sélecteur.</summary>
    public string ReleaseCountText { get; }

    /// <summary>
    /// Vrai quand la fiche publie des releases qu'aucun tag ne rattache à cette série de jeu. Le
    /// dévoilement n'est offert que dans ce cas : ailleurs, il n'aurait rien de plus à montrer.
    /// </summary>
    public bool HasIncompatibleReleases { get; }

    /// <summary>
    /// Vrai quand toutes les versions sont listées. L'état de départ est faux, SAUF quand aucune
    /// release n'est déclarée compatible : voir le constructeur, qui explique pourquoi ouvrir sur
    /// une liste vide serait pire qu'ouvrir sur la liste complète avec son avertissement.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleAllReleasesText))]
    private bool _showsAllReleases;

    /// <summary>Libellé du bouton de dévoilement, qui dit ce que le clic va faire.</summary>
    public string ToggleAllReleasesText
        => ShowsAllReleases ? UiText.Mods.ShowCompatibleReleasesOnly : UiText.Mods.ShowAllReleases;

    /// <summary>Faux quand une seule release convient : le sélecteur n'a alors rien à offrir.</summary>
    public bool HasReleaseChoice => _compatibleReleases.Length > 1 || HasIncompatibleReleases;

    /// <summary>Release choisie. La changer recalcule le plan, dépendances comprises.</summary>
    [ObservableProperty]
    private ModReleaseChoiceViewModel? _selectedRelease;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    /// <summary>Nom du fichier qui sera écrit, en monospace comme toute valeur machine.</summary>
    [ObservableProperty]
    private string _fileNameText = string.Empty;

    [ObservableProperty]
    private bool _showApproximateWarning;

    [ObservableProperty]
    private string _approximateWarning = string.Empty;

    /// <summary>Avertissement d'une release qu'aucun tag ne déclare compatible. NON bloquant.</summary>
    [ObservableProperty]
    private bool _showIncompatibleWarning;

    [ObservableProperty]
    private string _incompatibleWarning = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyDependencyNews))]
    private IReadOnlyList<ModDependencyChoiceViewModel> _dependencies = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyDependencyNews))]
    private bool _hasDependencies;

    /// <summary>
    /// Dépendances publiées sans release compatible, proposées à l'installation malgré tout, avec
    /// leur avertissement ambré attaché. Cochées d'avance comme les autres : voir
    /// <see cref="ModDependencyChoiceViewModel.ForInstallAnyway"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoCompatibleReleaseWithoutOffer))]
    [NotifyPropertyChangedFor(nameof(HasAnyDependencyNews))]
    private IReadOnlyList<ModDependencyChoiceViewModel> _installableAnyway = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoCompatibleReleaseWithoutOffer))]
    [NotifyPropertyChangedFor(nameof(HasAnyDependencyNews))]
    private bool _hasInstallableAnyway;

    /// <summary>Dépendances dont le ModDB ne publie aucune fiche : le seul cas « introuvable ».</summary>
    [ObservableProperty]
    private string _unresolvedMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyDependencyNews))]
    private bool _hasUnresolved;

    /// <summary>Dépendances publiées sur le ModDB, mais sans release pour cette version du jeu.</summary>
    [ObservableProperty]
    private string _noCompatibleReleaseMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoCompatibleReleaseWithoutOffer))]
    [NotifyPropertyChangedFor(nameof(HasAnyDependencyNews))]
    private bool _hasNoCompatibleRelease;

    /// <summary>
    /// Le bandeau « aucune release compatible » quand il n'y a RIEN à cocher en dessous. Sinon il
    /// est rendu juste au-dessus des cases « installer quand même », auxquelles il se rapporte :
    /// l'afficher deux fois, ou loin d'elles, en ferait un avertissement flottant.
    /// </summary>
    public bool HasNoCompatibleReleaseWithoutOffer => HasNoCompatibleRelease && !HasInstallableAnyway;

    [ObservableProperty]
    private string _disabledMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyDependencyNews))]
    private bool _hasDisabled;

    /// <summary>
    /// Vrai dès que le volet des dépendances a quelque chose à montrer. Faux, il affiche « ce mod
    /// n'a besoin de rien d'autre » : un volet vide poserait la question sans y répondre.
    /// </summary>
    public bool HasAnyDependencyNews
        => HasDependencies || HasInstallableAnyway || HasUnresolved || HasNoCompatibleRelease || HasDisabled;

    /// <summary>Vrai pendant l'application du plan ou pendant le recalcul déclenché par le sélecteur.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Vrai quand le dernier recalcul de plan a échoué : le plan affiché est alors le précédent.</summary>
    [ObservableProperty]
    private bool _reloadFailed;

    /// <summary>Le recalcul en vol, pour que les tests headless l'attendent sans sondage arbitraire.</summary>
    internal Task ReloadCompletion { get; private set; } = Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// Sur <see cref="_allReleases"/> et non sur <see cref="Releases"/> : chaque
    /// <see cref="ModReleaseChoiceViewModel"/> est construit dès le constructeur, changelog analysé
    /// compris, y compris ceux que le dévoilement n'a jamais montrés. Libérer la seule liste
    /// AFFICHÉE laissait donc derrière soi tout ce qu'un dialogue fermé sans dévoilement avait
    /// décodé.
    /// </remarks>
    public void Dispose()
    {
        foreach (var release in _allReleases)
        {
            release.Dispose();
        }

        Thumbnail.Dispose();
        DisposeDependencyRows();
    }

    private void DisposeDependencyRows()
    {
        foreach (var dependency in Dependencies.Concat(InstallableAnyway))
        {
            dependency.Dispose();
        }
    }

    partial void OnSelectedReleaseChanged(ModReleaseChoiceViewModel? value)
    {
        if (_isApplyingPlan || value is null || value.ReleaseId == Plan.Primary.Release.ReleaseId)
        {
            return;
        }

        ReloadCompletion = ReloadAsync(value.ReleaseId);
    }

    private async Task ReloadAsync(int releaseId)
    {
        IsBusy = true;
        ReloadFailed = false;
        try
        {
            ApplyPlan(await _reprepare(releaseId).ConfigureAwait(true));
        }
        catch (Exception exception) when (exception is ModDbApiException or ModDbUnavailableException
            or Core.Http.DownloadFailedException or ModInstallFailedException or ModReleaseNotFoundException)
        {
            // Le plan précédent reste affiché et reste applicable : un réseau qui tombe pendant
            // qu'on compare deux versions ne doit pas vider le dialogue de son contenu.
            ReloadFailed = true;
            _isApplyingPlan = true;
            SelectedRelease = Releases.FirstOrDefault(release => release.ReleaseId == Plan.Primary.Release.ReleaseId);
            _isApplyingPlan = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Toutes les propriétés dérivées du plan sont reposées ici, et nulle part ailleurs : c'est ce
    // qui garantit qu'un changement de release rafraîchit le dialogue ENTIER (fichier cible,
    // avertissement d'approximation, dépendances manquantes, non résolues, désactivées) plutôt
    // qu'une moitié.
    private void ApplyPlan(ModInstallPlan plan)
    {
        _isApplyingPlan = true;
        try
        {
            Plan = plan;

            // « Remplacer » plutôt qu'« installer » dès qu'une copie est déjà là : c'est ce que
            // l'opération fait réellement depuis qu'elle suit la discipline de la mise à jour, et
            // l'écran d'un joueur qui change de version de mod ne doit pas lui laisser croire qu'il
            // en ajoute une deuxième à côté.
            Title = plan.IsReplacement
                ? UiText.Mods.ReplacePlanTitle(plan.Primary.DisplayName)
                : UiText.Mods.PlanTitle(plan.Primary.DisplayName);
            Message = plan.IsReplacement
                ? UiText.Mods.ReplacePlanMessage(
                    plan.ExistingVersion?.ToString() ?? string.Empty,
                    plan.Primary.Version.ToString(),
                    _instanceName)
                : UiText.Mods.PlanMessage(plan.Primary.Version.ToString(), _instanceName);
            FileNameText = plan.Primary.TargetFileName;

            // Les deux avertissements sont exclusifs et disent deux choses différentes : « même
            // série, pas cochée » n'est pas « pas déclarée du tout ».
            ShowIncompatibleWarning = plan.Primary.IsDeclaredIncompatible;
            IncompatibleWarning = UiText.Mods.IncompatibleReleaseWarning(
                plan.GameVersion.ToString(),
                plan.Primary.Release.CompatibleGameVersionTags);

            ShowApproximateWarning = plan.Primary.IsApproximateMatch && !plan.Primary.IsDeclaredIncompatible;
            ApproximateWarning = UiText.Mods.ApproximateWarning(plan.GameVersion.ToString());

            // Un changement de release change les dépendances (elles se lisent dans le modinfo.json
            // de l'archive téléchargée) : les anciennes lignes sont jetées, donc disposées, sinon
            // chaque aller-retour dans le sélecteur laisserait une vignette en vol derrière lui.
            DisposeDependencyRows();

            Dependencies = plan.MissingDependencies
                .Select(item => new ModDependencyChoiceViewModel(item, plan.Issues, _logoDirectory, _images))
                .ToArray();
            HasDependencies = Dependencies.Count > 0;

            InstallableAnyway = plan.InstallableAnyway
                .Select(dependency => ModDependencyChoiceViewModel.ForInstallAnyway(dependency, _logoDirectory, _images))
                .ToArray();
            HasInstallableAnyway = InstallableAnyway.Count > 0;

            // Les DEUX verdicts de la résolution, distincts et reposés ensemble : changer de
            // release peut faire passer une dépendance de « introuvable sur le ModDB » à « publiée
            // mais sans release pour cette version du jeu », et l'inverse.
            var (notOnModDb, withoutRelease) = UnresolvedDependencyMessages.Build(plan.UnresolvedDependencies, plan.GameVersion);
            UnresolvedMessage = notOnModDb;
            HasUnresolved = notOnModDb.Length > 0;
            NoCompatibleReleaseMessage = withoutRelease;
            HasNoCompatibleRelease = withoutRelease.Length > 0;

            var disabled = plan.Issues.Where(issue => issue.Status == ModDependencyStatus.Disabled).Select(issue => issue.ModIdString).ToArray();
            DisabledMessage = UiText.Mods.DisabledDependencies(disabled);
            HasDisabled = disabled.Length > 0;

            SelectedRelease = Releases.FirstOrDefault(release => release.ReleaseId == plan.Primary.Release.ReleaseId)
                ?? SelectedRelease;
        }
        finally
        {
            _isApplyingPlan = false;
        }
    }

    /// <summary>
    /// Dévoile ou remasque les releases qu'aucun tag ne déclare compatibles. Ne change RIEN au plan
    /// courant : il faut encore en choisir une pour que quoi que ce soit se recalcule.
    /// </summary>
    [RelayCommand]
    private void ToggleAllReleases()
    {
        ShowsAllReleases = !ShowsAllReleases;
        Releases = ShowsAllReleases ? _allReleases : _compatibleReleases;

        // Remasquer alors qu'une release incompatible est choisie la laisserait sélectionnée sans
        // être listée : le dévoilement reste alors ouvert, ce qui est plus honnête que de la
        // faire disparaître de l'écran tout en l'installant.
        if (!ShowsAllReleases && SelectedRelease is { IsDeclaredIncompatible: true })
        {
            ShowsAllReleases = true;
            Releases = _allReleases;
        }
    }

    // Comme la fiche : l'overlay dispose ce qu'il ferme, un second Dispose ici relancerait un
    // Cancel sur des sources d'annulation déjà libérées.
    [RelayCommand]
    private void Cancel() => _overlay.Close();

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        IsBusy = true;
        try
        {
            var selected = Dependencies.Concat(InstallableAnyway)
                .Where(dependency => dependency.IsSelected)
                .Select(dependency => dependency.ModIdString)
                .ToArray();

            await _confirm(Plan, selected).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// Une release proposée par le sélecteur de version : son numéro, sa date, ses téléchargements et
/// son changelog.
/// </summary>
/// <remarks>
/// Le changelog est du HTML, ce que rien dans l'API ne dit : le relevé réel montre des
/// <c>&lt;ul&gt;</c>/<c>&lt;li&gt;</c> et du <c>&lt;strong&gt;</c> dans ce champ. Il passe donc par
/// le même parseur et le même rendu que la description de la fiche, plutôt que d'afficher des
/// balises à l'utilisateur.
/// </remarks>
public sealed class ModReleaseChoiceViewModel : IDisposable
{
    /// <summary>Largeur d'affichage des illustrations d'un changelog, en pixels.</summary>
    private const int ChangelogWidth = 420;

    /// <summary>Construit la ligne.</summary>
    /// <param name="choice">Release retenue par <see cref="ModReleaseSelector.SelectAll"/>.</param>
    /// <param name="urlOpener">Ouverture des liens du changelog.</param>
    /// <param name="images">Cache d'images.</param>
    public ModReleaseChoiceViewModel(ModReleaseChoice choice, IExternalUrlOpener urlOpener, IModLogoCache images)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(urlOpener);
        ArgumentNullException.ThrowIfNull(images);

        ReleaseId = choice.Release.ReleaseId;
        VersionText = choice.Release.Version.ToString();
        DateText = UiText.Mods.ReleaseDate(choice.Release.CreatedUtc);
        DownloadsText = UiText.Mods.FormatCount(choice.Release.Downloads);
        GameVersionsText = UiText.Mods.CompatibleVersions(choice.Release.CompatibleGameVersionTags);

        // Deux pastilles distinctes pour deux faits distincts : « même série, pas cochée » et
        // « pas déclarée du tout ». La seconde n'apparaît qu'après le dévoilement.
        IsApproximate = choice.Compatibility == ModReleaseCompatibility.SameMinorSeries;
        IsDeclaredIncompatible = choice.IsDeclaredIncompatible;
        CompatibilityTag = IsDeclaredIncompatible
            ? UiText.Mods.IncompatibleReleaseTag
            : UiText.Mods.ApproximateReleaseTag;

        Changelog = new RichTextDocumentViewModel(
            HtmlRichTextParser.Parse(choice.Release.Changelog),
            urlOpener,
            images,
            ChangelogWidth);
        HasChangelog = !Changelog.IsEmpty;
    }

    /// <summary>Identifiant de la release, celui qu'on réinjecte dans la préparation du plan.</summary>
    public int ReleaseId { get; }

    /// <summary>Numéro de version du mod.</summary>
    public string VersionText { get; }

    /// <summary>Date de publication, vide si l'API ne l'a pas rendue lisible.</summary>
    public string DateText { get; }

    /// <summary>Décompte de téléchargements, séparateur de milliers de la langue.</summary>
    public string DownloadsText { get; }

    /// <summary>Versions de jeu que l'auteur a réellement cochées, déjà résumées quand elles sont nombreuses.</summary>
    public string GameVersionsText { get; }

    /// <summary>Vrai si cette release a été retenue par élargissement à la série mineure.</summary>
    public bool IsApproximate { get; }

    /// <summary>Vrai si aucun tag ne rattache cette release à la série de jeu de l'instance.</summary>
    public bool IsDeclaredIncompatible { get; }

    /// <summary>Vrai dès que la compatibilité n'est pas affirmée par l'auteur : la pastille s'affiche.</summary>
    public bool HasCompatibilityTag => IsApproximate || IsDeclaredIncompatible;

    /// <summary>Libellé de la pastille : approximation ou absence de déclaration.</summary>
    public string CompatibilityTag { get; }

    /// <summary>Notes de version, rendues comme la description de la fiche.</summary>
    public RichTextDocumentViewModel Changelog { get; }

    /// <summary>Faux quand l'auteur n'a rien publié pour cette release, ce qui est courant.</summary>
    public bool HasChangelog { get; }

    /// <inheritdoc />
    public void Dispose() => Changelog.Dispose();
}

/// <summary>
/// Rend les deux verdicts de <see cref="UnresolvedModDependency"/> en deux phrases distinctes,
/// partagées par le dialogue d'installation et celui de mise à jour. « Introuvable sur le ModDB »
/// n'est dit que d'une dépendance dont la fiche n'existe VRAIMENT pas ; une fiche publiée dont
/// aucune release ne cible la version de jeu de l'instance a son propre texte, et se nomme par le
/// nom de sa fiche puisqu'on l'a.
/// </summary>
internal static class UnresolvedDependencyMessages
{
    public static (string NotOnModDb, string WithoutCompatibleRelease) Build(
        IReadOnlyList<UnresolvedModDependency> unresolved,
        GameVersion gameVersion)
    {
        var missing = unresolved
            .Where(dependency => dependency.Reason == ModDependencyResolution.NotOnModDb)
            .Select(dependency => dependency.ModIdString)
            .ToArray();

        var incompatible = unresolved
            .Where(dependency => dependency.Reason == ModDependencyResolution.NoCompatibleRelease)
            .Select(dependency => dependency.DisplayName)
            .ToArray();

        return (
            UiText.Mods.DependenciesNotOnModDb(missing),
            UiText.Mods.DependenciesWithoutCompatibleRelease(incompatible, gameVersion.ToString()));
    }
}

/// <summary>Une dépendance manquante, cochable, dans le dialogue de confirmation.</summary>
public sealed partial class ModDependencyChoiceViewModel : ObservableObject, IDisposable
{
    /// <summary>Construit la ligne.</summary>
    /// <param name="item">Release résolue pour cette dépendance.</param>
    /// <param name="issues">Diagnostic complet, pour expliquer pourquoi elle est proposée.</param>
    /// <param name="logoDirectory">Annuaire des logos, ou <see langword="null"/> pour une ligne sans vignette.</param>
    /// <param name="images">Cache d'images, ou <see langword="null"/> pour une ligne sans vignette.</param>
    /// <remarks>
    /// Les deux derniers paramètres sont optionnels, et c'est le seul endroit du chantier où ils le
    /// sont : une dépendance se nomme dans plusieurs dialogues, et un test qui vérifie un LIBELLÉ
    /// n'a aucune raison de câbler de quoi charger une image.
    /// </remarks>
    public ModDependencyChoiceViewModel(
        ModInstallItem item,
        IReadOnlyList<ModDependencyIssue> issues,
        IModLogoDirectory? logoDirectory = null,
        IModLogoCache? images = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(issues);

        ModIdString = item.ModIdString;
        DisplayName = item.DisplayName;
        VersionText = item.Version.ToString();
        Thumbnail = CreateThumbnail(item.ModDbModId, logoDirectory, images);

        var issue = issues.FirstOrDefault(candidate => string.Equals(candidate.ModIdString, item.ModIdString, StringComparison.OrdinalIgnoreCase));
        ReasonText = UiText.Mods.DependencyReason(issue);
    }

    private ModDependencyChoiceViewModel(UnresolvedModDependency dependency, IModLogoDirectory? logoDirectory, IModLogoCache? images)
    {
        var item = dependency.BestAvailable!;
        ModIdString = dependency.ModIdString;
        DisplayName = dependency.DisplayName;
        VersionText = item.Version.ToString();
        ReasonText = UiText.Mods.InstallAnywayReason(dependency.BestAvailableGameVersions);
        IsDeclaredIncompatible = true;
        Thumbnail = CreateThumbnail(item.ModDbModId, logoDirectory, images);
    }

    private static ModThumbnailViewModel CreateThumbnail(int modDbModId, IModLogoDirectory? logoDirectory, IModLogoCache? images)
        => logoDirectory is null || images is null
            ? ModThumbnailViewModel.None
            : new ModThumbnailViewModel(modDbModId, logoDirectory, images);

    /// <summary>
    /// Ligne « installer quand même » d'une dépendance publiée sans release compatible : même
    /// contrôle, étiquetée de ses vraies versions taguées et de son avertissement ambré.
    /// </summary>
    /// <remarks>
    /// Cochée d'avance, comme les autres, et c'est un renversement assumé. L'argument d'avant était
    /// qu'une case pré-cochée convient pour ce que l'auteur a déclaré et pas pour ce qu'il n'a pas
    /// déclaré. Sauf qu'une dépendance déclarée par un modinfo.json est NÉCESSAIRE au fonctionnement
    /// du mod : décochée, l'installation aboutit sur un jeu qui ne démarre pas, et l'utilisateur ne
    /// fait pas le lien. Le consentement reste entier — la case se décoche, l'avertissement reste
    /// attaché à la ligne, et la provenance continue de marquer l'installation
    /// (<c>ModProvenance.DeclaredIncompatible</c>) — mais le défaut devient « ça marchera ».
    /// </remarks>
    public static ModDependencyChoiceViewModel ForInstallAnyway(
        UnresolvedModDependency dependency,
        IModLogoDirectory? logoDirectory = null,
        IModLogoCache? images = null)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        if (dependency.BestAvailable is null)
        {
            throw new ArgumentException("Cette dépendance n'a aucune release à proposer.", nameof(dependency));
        }

        return new ModDependencyChoiceViewModel(dependency, logoDirectory, images);
    }

    public string ModIdString { get; }

    public string DisplayName { get; }

    public string VersionText { get; }

    /// <summary>Vignette de la dépendance, ou le pictogramme générique.</summary>
    public ModThumbnailViewModel Thumbnail { get; }

    /// <summary>Pourquoi cette dépendance est proposée : absente, trop ancienne, signalée par le ModDB.</summary>
    public string ReasonText { get; }

    /// <summary>Vrai pour une ligne « installer quand même » : l'auteur n'a rien déclaré pour cette version du jeu.</summary>
    public bool IsDeclaredIncompatible { get; }

    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>Annule le chargement de vignette encore en vol.</summary>
    public void Dispose() => Thumbnail.Dispose();
}