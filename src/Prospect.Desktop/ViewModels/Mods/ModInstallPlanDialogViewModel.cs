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
    private readonly string _instanceName;

    private bool _isApplyingPlan;

    /// <summary>Construit le dialogue.</summary>
    /// <param name="plan">Plan produit par <see cref="ModInstallService.PrepareAsync"/>.</param>
    /// <param name="instanceName">Nom de l'instance cible, montré dans le titre.</param>
    /// <param name="confirm">Applique le plan COURANT avec les dépendances retenues.</param>
    /// <param name="reprepare">Recalcule le plan pour une autre release.</param>
    /// <param name="overlay">Panneau modal, pour se refermer.</param>
    /// <param name="urlOpener">Ouverture des liens présents dans un changelog.</param>
    /// <param name="images">Cache d'images, pour les illustrations d'un changelog.</param>
    public ModInstallPlanDialogViewModel(
        ModInstallPlan plan,
        string instanceName,
        Func<ModInstallPlan, IReadOnlyCollection<string>, Task> confirm,
        Func<int, Task<ModInstallPlan>> reprepare,
        IOverlayService overlay,
        IExternalUrlOpener urlOpener,
        IModLogoCache images)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(reprepare);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(urlOpener);
        ArgumentNullException.ThrowIfNull(images);

        _confirm = confirm;
        _reprepare = reprepare;
        _overlay = overlay;
        _instanceName = instanceName;

        _plan = plan;
        Releases = plan.AvailableReleases
            .Select(choice => new ModReleaseChoiceViewModel(choice, urlOpener, images))
            .ToArray();
        ReleaseCountText = UiText.Mods.ReleaseChoiceCount(Releases.Count);
        HasReleaseChoice = Releases.Count > 1;

        // ApplyPlan présélectionne la release du plan, c'est-à-dire la plus récente compatible :
        // le comportement d'avant le sélecteur, à l'identique tant qu'on n'y touche pas.
        ApplyPlan(plan);
    }

    /// <summary>Plan courant : celui du dernier calcul, pas forcément celui de la construction.</summary>
    [ObservableProperty]
    private ModInstallPlan _plan;

    /// <summary>Releases compatibles proposées, la plus récente d'abord.</summary>
    public IReadOnlyList<ModReleaseChoiceViewModel> Releases { get; }

    /// <summary>Décompte affiché au-dessus du sélecteur.</summary>
    public string ReleaseCountText { get; }

    /// <summary>Faux quand une seule release convient : le sélecteur n'a alors rien à offrir.</summary>
    public bool HasReleaseChoice { get; }

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

    [ObservableProperty]
    private IReadOnlyList<ModDependencyChoiceViewModel> _dependencies = [];

    [ObservableProperty]
    private bool _hasDependencies;

    /// <summary>Dépendances dont le ModDB ne publie aucune fiche : le seul cas « introuvable ».</summary>
    [ObservableProperty]
    private string _unresolvedMessage = string.Empty;

    [ObservableProperty]
    private bool _hasUnresolved;

    /// <summary>Dépendances publiées sur le ModDB, mais sans release pour cette version du jeu.</summary>
    [ObservableProperty]
    private string _noCompatibleReleaseMessage = string.Empty;

    [ObservableProperty]
    private bool _hasNoCompatibleRelease;

    [ObservableProperty]
    private string _disabledMessage = string.Empty;

    [ObservableProperty]
    private bool _hasDisabled;

    /// <summary>Vrai pendant l'application du plan ou pendant le recalcul déclenché par le sélecteur.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Vrai quand le dernier recalcul de plan a échoué : le plan affiché est alors le précédent.</summary>
    [ObservableProperty]
    private bool _reloadFailed;

    /// <summary>Le recalcul en vol, pour que les tests headless l'attendent sans sondage arbitraire.</summary>
    internal Task ReloadCompletion { get; private set; } = Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var release in Releases)
        {
            release.Dispose();
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
            Title = UiText.Mods.PlanTitle(plan.Primary.DisplayName);
            Message = UiText.Mods.PlanMessage(plan.Primary.Version.ToString(), _instanceName);
            FileNameText = plan.Primary.TargetFileName;

            ShowApproximateWarning = plan.Primary.IsApproximateMatch;
            ApproximateWarning = UiText.Mods.ApproximateWarning(plan.GameVersion.ToString());

            Dependencies = plan.MissingDependencies
                .Select(item => new ModDependencyChoiceViewModel(item, plan.Issues))
                .ToArray();
            HasDependencies = Dependencies.Count > 0;

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

    [RelayCommand]
    private void Cancel()
    {
        _overlay.Close();
        Dispose();
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        IsBusy = true;
        try
        {
            var selected = Dependencies
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
        IsApproximate = choice.IsApproximate;
        ApproximateTag = UiText.Mods.ApproximateReleaseTag;

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

    /// <summary>Vrai si cette release a été retenue par élargissement à la série mineure.</summary>
    public bool IsApproximate { get; }

    /// <summary>Libellé de la pastille d'approximation.</summary>
    public string ApproximateTag { get; }

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
public sealed partial class ModDependencyChoiceViewModel : ObservableObject
{
    /// <summary>Construit la ligne.</summary>
    /// <param name="item">Release résolue pour cette dépendance.</param>
    /// <param name="issues">Diagnostic complet, pour expliquer pourquoi elle est proposée.</param>
    public ModDependencyChoiceViewModel(ModInstallItem item, IReadOnlyList<ModDependencyIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(issues);

        ModIdString = item.ModIdString;
        DisplayName = item.DisplayName;
        VersionText = item.Version.ToString();

        var issue = issues.FirstOrDefault(candidate => string.Equals(candidate.ModIdString, item.ModIdString, StringComparison.OrdinalIgnoreCase));
        ReasonText = UiText.Mods.DependencyReason(issue);
    }

    public string ModIdString { get; }

    public string DisplayName { get; }

    public string VersionText { get; }

    /// <summary>Pourquoi cette dépendance est proposée : absente, trop ancienne, signalée par le ModDB.</summary>
    public string ReasonText { get; }

    [ObservableProperty]
    private bool _isSelected = true;
}