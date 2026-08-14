using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.Http;
using Prospect.Core.Instances;
using Prospect.Core.ModDb;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Mods;

/// <summary>
/// ViewModel du navigateur de mods (design/ui_kits/launcher/screen-mods.jsx) : recherche plein
/// texte, tags de catégorie, sélecteur d'instance cible qui pilote le filtre et les badges de
/// compatibilité, tri, fiche en dialog, et l'état hors ligne de la maquette.
/// </summary>
/// <remarks>
/// <para>
/// La recherche et le tri se font ENTIÈREMENT en mémoire (<see cref="ModCatalogSearch"/>) : l'API
/// ne pagine rien, le catalogue entier arrive en un appel et repart à chaque frappe serait à la
/// fois plus lent et impoli. Seul l'index de compatibilité vient du serveur, qui est le seul à
/// savoir quelle release est taguée pour quelle version de jeu.
/// </para>
/// <para>
/// Le RENDU, lui, est paresseux. La recherche produit toujours la liste complète des
/// correspondances (<see cref="MatchCount"/>), mais <see cref="Results"/> n'en expose qu'une
/// fenêtre, étendue par tranches quand l'utilisateur approche du bas du défilement et remise à
/// zéro à chaque changement de recherche, de tag, de tri ou d'instance cible. Sans cette fenêtre,
/// le catalogue réel (8 026 fiches) construisait 8 026 cartes d'un coup, donc autant de
/// téléchargements et de décodages de logos : mesuré, près de 8 Gio de jeu de travail et une
/// dizaine de secondes de mise en page par frappe.
/// </para>
/// <para>
/// Et cette fenêtre est PLAFONNÉE (<see cref="MaxRenderedResults"/>), ce qu'elle n'était pas.
/// L'extension par tranches bornait le rendu INITIAL sans rien borner du tout ensuite : défiler
/// suffisait à matérialiser tout le catalogue, une tranche à la fois, et à retomber exactement dans
/// le cas que la fenêtre devait éviter. La grille ne virtualise pas, c'est un choix documenté
/// (<see cref="Prospect.Desktop.Layout.FluidGridPanel"/>) qui repose sur la promesse que ce
/// ViewModel ne lui confie qu'une poignée de cartes : le plafond est ce qui rend cette promesse
/// vraie. Mesuré par le harnais de charge de ce chantier, sur une fenêtre laissée libre : 800 cartes
/// rendues d'affilée coûtaient 392 Mio de mémoire gérée (environ 490 Kio par carte, l'arbre de
/// contrôles et ses mises en forme de texte), et le jeu de travail passait de 177 à 644 Mio. Au-delà
/// du plafond, le compteur sous la grille le DIT et invite à affiner la recherche, plutôt que de
/// laisser croire que le défilement continue.
/// </para>
/// </remarks>
public sealed partial class ModBrowserViewModel : ObservableObject
{
    /// <summary>
    /// Nombre de cartes rendues d'emblée, et taille de chaque extension. Une trentaine remplit
    /// largement la plus grande fenêtre prévue par la garde de mise en page (1280x800 montre au
    /// plus une douzaine de cartes de 300x204), ce qui laisse de quoi défiler avant que
    /// l'extension suivante ne soit demandée.
    /// </summary>
    public const int RenderWindowSize = 30;

    /// <summary>
    /// Nombre maximal de cartes rendues d'un coup, quelle que soit la longueur du défilement. Cinq
    /// tranches, soit une douzaine d'écrans à la plus grande taille de fenêtre prévue par la garde
    /// de mise en page : bien au-delà de ce qu'un défilement ordinaire atteint, et assez bas pour
    /// que le pire cas reste de l'ordre d'une centaine de mégaoctets (mesuré : 96 Mio d'arbre
    /// visuel, rendus dès qu'on quitte la page) plutôt que de plusieurs centaines qui ne
    /// s'arrêtaient nulle part. C'est un plafond FRANC, dans l'esprit de celui
    /// d'<c>IModLogoCache</c> : au-delà, l'écran ne rend plus rien de nouveau et le dit, plutôt que
    /// de retirer des cartes déjà affichées sous le curseur de l'utilisateur.
    /// </summary>
    public const int MaxRenderedResults = 5 * RenderWindowSize;

    private readonly IModDbClient _client;
    private readonly ModInstallService _installService;
    private readonly IInstalledModRepository _installedMods;
    private readonly IInstanceRepository _instances;
    private readonly IExternalUrlOpener _urlOpener;
    private readonly IOverlayService _overlay;
    private readonly IToastService _toasts;
    private readonly IModLogoCache _logoCache;
    private readonly IModLogoDirectory _logoDirectory;

    private IReadOnlyList<ModDbModSummary> _catalog = [];
    private IReadOnlyList<ModDbModSummary> _matches = [];
    private ModDbCompatibilityIndex? _compatibilityIndex;
    private InstalledModIndex _installedIndex = InstalledModIndex.Empty;

    public ModBrowserViewModel(
        IModDbClient client,
        ModInstallService installService,
        IInstalledModRepository installedMods,
        IInstanceRepository instances,
        IExternalUrlOpener urlOpener,
        IOverlayService overlay,
        IToastService toasts,
        IModLogoCache logoCache,
        IModLogoDirectory logoDirectory)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(installService);
        ArgumentNullException.ThrowIfNull(installedMods);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(urlOpener);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(logoCache);
        ArgumentNullException.ThrowIfNull(logoDirectory);

        _client = client;
        _installService = installService;
        _installedMods = installedMods;
        _instances = instances;
        _urlOpener = urlOpener;
        _overlay = overlay;
        _toasts = toasts;
        _logoCache = logoCache;
        _logoDirectory = logoDirectory;
    }

    /// <summary>
    /// Fenêtre de cartes réellement construites et rendues. Ce n'est PAS l'ensemble des résultats :
    /// voir <see cref="MatchCount"/> pour le total et <see cref="ShowMoreCommand"/> pour l'étendre.
    /// </summary>
    public ObservableCollection<ModCardViewModel> Results { get; } = [];

    /// <summary>Catégories proposées en tags sous la barre de recherche.</summary>
    public ObservableCollection<ModTagViewModel> Tags { get; } = [];

    /// <summary>Instances proposées comme cible d'installation, « Toutes les versions » compris.</summary>
    public ObservableCollection<ModTargetInstanceViewModel> TargetInstances { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => Rebuild();

    [ObservableProperty]
    private string? _activeTagName;

    /// <summary>0 = téléchargements, 1 = mise à jour, 2 = tendance, 3 = nom. Pont vers le <c>ComboBox</c>.</summary>
    [ObservableProperty]
    private int _sortIndex;

    partial void OnSortIndexChanged(int value) => Rebuild();

    /// <summary>Instance dont la version de jeu pilote les badges et le filtre de compatibilité.</summary>
    [ObservableProperty]
    private ModTargetInstanceViewModel? _selectedInstance;

    [ObservableProperty]
    private string _subtitleText = string.Empty;

    /// <summary>Bandeau « ModDB injoignable » de la maquette, avec son message exact.</summary>
    [ObservableProperty]
    private bool _isOffline;

    [ObservableProperty]
    private string? _cacheWarning;

    [ObservableProperty]
    private bool _showEmptyState;

    /// <summary>Nombre total de mods correspondant à la recherche, cartes rendues ou non.</summary>
    [ObservableProperty]
    private int _matchCount;

    /// <summary>Vrai tant que la fenêtre de rendu ne couvre pas tous les résultats.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowMoreCommand))]
    private bool _hasMoreResults;

    /// <summary>Compteur « X sur Y affichés », sous la grille.</summary>
    [ObservableProperty]
    private string _shownCountText = string.Empty;

    /// <summary>
    /// Vrai quand la grille a atteint <see cref="MaxRenderedResults"/> alors qu'il reste des
    /// correspondances : le défilement ne rendra plus rien, et <see cref="ShownCountText"/> le dit.
    /// Exposé pour que la borne se teste sans passer par le libellé traduit.
    /// </summary>
    [ObservableProperty]
    private bool _isRenderCapped;

    [ObservableProperty]
    private string _emptyStateTitle = UiText.Mods.EmptyResultsTitle;

    [ObservableProperty]
    private string _emptyStateDescription = string.Empty;

    /// <summary>Vrai quand une préparation d'installation est en cours.</summary>
    [ObservableProperty]
    private bool _isInstalling;

    /// <summary>Progression du téléchargement en cours, entre 0 et 1, ou <see langword="null"/>.</summary>
    [ObservableProperty]
    private double? _installProgress;

    /// <summary>
    /// Préselectionne l'instance cible : appelé par le bouton « Parcourir le ModDB » de l'onglet
    /// Mods d'une instance, pour que l'écran s'ouvre déjà filtré sur elle.
    /// </summary>
    public string? PendingInstanceSlug { get; set; }

    /// <summary>Charge catalogue, tags et instances. Rejoue le dernier filtre si l'écran était déjà chargé.</summary>
    [RelayCommand]
    private Task InitializeAsync(CancellationToken cancellationToken = default) => LoadAsync(forceRefresh: false, cancellationToken);

    /// <summary>« Actualiser l'index » : ignore le cache et réinterroge l'API.</summary>
    [RelayCommand]
    private Task RefreshAsync(CancellationToken cancellationToken = default) => LoadAsync(forceRefresh: true, cancellationToken);

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private async Task ToggleTagAsync(ModTagViewModel tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        ActiveTagName = string.Equals(ActiveTagName, tag.Name, StringComparison.Ordinal) ? null : tag.Name;
        foreach (var candidate in Tags)
        {
            candidate.IsActive = string.Equals(candidate.Name, ActiveTagName, StringComparison.Ordinal);
        }

        Rebuild();
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private async Task LoadAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            await LoadInstancesAsync(cancellationToken).ConfigureAwait(true);
            await LoadCatalogAsync(forceRefresh, cancellationToken).ConfigureAwait(true);
            await LoadCompatibilityAsync(cancellationToken).ConfigureAwait(true);
            await LoadInstalledAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
            Rebuild();
        }
    }

    private async Task LoadInstancesAsync(CancellationToken cancellationToken)
    {
        var scan = await _instances.ScanAsync(cancellationToken).ConfigureAwait(true);
        var previous = SelectedInstance?.Slug ?? PendingInstanceSlug;

        TargetInstances.Clear();
        TargetInstances.Add(ModTargetInstanceViewModel.AllVersions);
        foreach (var instance in scan.Instances.OrderBy(entry => entry.Metadata.Name, StringComparer.OrdinalIgnoreCase))
        {
            TargetInstances.Add(new ModTargetInstanceViewModel(instance.Slug, instance.Metadata.Name, instance.Metadata.GameVersion));
        }

        // Une instance réelle est présélectionnée par défaut, comme dans la maquette où le filtre
        // s'ouvre déjà sur « Compatible 1.20.4 » : sans cible, le bouton Installer n'aurait nulle
        // part où poser le mod, et l'écran s'ouvrirait sur un choix à faire plutôt que sur du
        // contenu. « Toutes les versions » reste à un clic.
        SelectedInstance = (previous is null ? null : TargetInstances.FirstOrDefault(entry => entry.Slug == previous))
            ?? TargetInstances.FirstOrDefault(entry => entry.Slug is not null)
            ?? TargetInstances[0];
        PendingInstanceSlug = null;
    }

    private async Task LoadCatalogAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await _client.GetCatalogAsync(forceRefresh, cancellationToken).ConfigureAwait(true);
            _catalog = catalog.Mods;
            IsOffline = false;
            CacheWarning = catalog.Freshness == ModDbFreshness.Stale ? UiText.Mods.StaleCatalog : null;

            var tags = await _client.GetTagsAsync(forceRefresh, cancellationToken).ConfigureAwait(true);
            Tags.Clear();
            foreach (var tag in OrderByUsefulness(tags))
            {
                Tags.Add(new ModTagViewModel(tag, string.Equals(tag, ActiveTagName, StringComparison.Ordinal)));
            }
        }
        catch (ModDbUnavailableException)
        {
            // Le mode hors ligne de la maquette : bandeau explicite, et ce que le cache contient
            // encore. Ici il ne contient rien, d'où l'état vide dédié.
            _catalog = [];
            IsOffline = true;
            CacheWarning = null;
        }
    }

    /// <summary>
    /// Trie le vocabulaire de catégories par utilité décroissante : d'abord celles qui classent
    /// réellement le plus de mods du catalogue, puis l'ordre alphabétique à égalité.
    /// </summary>
    /// <remarks>
    /// L'ordre alphabétique seul convenait aux trois tags des doubles ; il ne convient pas aux 223
    /// du vrai <c>/api/tags</c>, où il met en tête des catégories anecdotiques (« Absolute Cinema »,
    /// « Added Tags Again Award ») et repousse loin celles qui servent vraiment. La maquette
    /// (design/ui_kits/launcher/screen-mods.jsx) montre une rangée d'une poignée de catégories
    /// utiles : c'est ce classement qui rend cette rangée fidèle une fois la vue limitée à une
    /// seule ligne défilante.
    /// </remarks>
    private IEnumerable<string> OrderByUsefulness(IEnumerable<ModDbTag> tags)
    {
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in _catalog.SelectMany(mod => mod.Tags))
        {
            usage[tag] = usage.GetValueOrDefault(tag) + 1;
        }

        return tags
            .Select(tag => tag.Name)
            .OrderByDescending(name => usage.GetValueOrDefault(name))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase);
    }

    private async Task LoadCompatibilityAsync(CancellationToken cancellationToken)
    {
        if (SelectedInstance?.GameVersion is not { } gameVersion)
        {
            _compatibilityIndex = null;

            return;
        }

        _compatibilityIndex = await _client
            .GetCompatibilityIndexAsync(gameVersion, widenToMinorSeries: false, cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Scanne les mods déjà présents dans l'instance ciblée. Aucun canal réseau nouveau : c'est le
    /// même scan de <c>data/Mods/</c> que l'onglet Mods d'une instance, croisé au catalogue par
    /// <see cref="InstalledModIndex"/>.
    /// </summary>
    private async Task LoadInstalledAsync(CancellationToken cancellationToken)
    {
        if (SelectedInstance?.Slug is not { } slug)
        {
            _installedIndex = InstalledModIndex.Empty;

            return;
        }

        var installed = await _installedMods.ScanAsync(slug, cancellationToken).ConfigureAwait(true);
        _installedIndex = InstalledModIndex.From(installed);
    }

    partial void OnSelectedInstanceChanged(ModTargetInstanceViewModel? value)
        => _ = ReloadForTargetAsync();

    private async Task ReloadForTargetAsync()
    {
        await LoadCompatibilityAsync(CancellationToken.None).ConfigureAwait(true);
        await LoadInstalledAsync(CancellationToken.None).ConfigureAwait(true);
        Rebuild();
    }

    /// <summary>
    /// Étend la fenêtre de rendu d'une tranche. Appelée automatiquement quand le défilement
    /// approche du bas (ModBrowserView.axaml.cs) et par le bouton de repli sous la grille.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasMoreResults))]
    private void ShowMore() => ExtendWindow();

    // La recherche, elle, porte toujours sur le catalogue COMPLET : seule la matérialisation des
    // cartes est fenêtrée.
    private void Rebuild()
    {
        var query = new ModCatalogQuery(
            SearchText,
            ActiveTagName,
            (ModCatalogSort)SortIndex,
            SelectedInstance?.GameVersion is null ? ModCompatibilityFilter.All : ModCompatibilityFilter.CompatibleOnly);

        _matches = ModCatalogSearch.Apply(_catalog, query, _compatibilityIndex);
        MatchCount = _matches.Count;

        // Chaque carte peut avoir un chargement de logo en vol (IModLogoCache) : sans ce Dispose,
        // reconstruire Results à chaque frappe de recherche laisserait une traînée de
        // téléchargements pour des cartes déjà jetées (même mécanique que HomeViewModel.RefreshAsync
        // pour InstanceCardViewModel).
        foreach (var previous in Results)
        {
            previous.Dispose();
        }

        Results.Clear();
        ExtendWindow();

        SubtitleText = UiText.Mods.Subtitle(_catalog.Count);
        ShowEmptyState = !IsLoading && _matches.Count == 0;
        EmptyStateTitle = IsOffline ? UiText.Mods.OfflineEmptyTitle : UiText.Mods.EmptyResultsTitle;
        EmptyStateDescription = IsOffline
            ? UiText.Mods.OfflineEmptyDescription
            : UiText.Mods.EmptyResultsDescription(SearchText);
    }

    private void ExtendWindow()
    {
        // Le plafond s'applique AVANT la tranche : une dernière tranche complète qui dépasserait le
        // plafond serait exactement la fuite qu'on borne ici.
        var ceiling = Math.Min(_matches.Count, MaxRenderedResults);
        var target = Math.Min(ceiling, Results.Count + RenderWindowSize);
        for (var index = Results.Count; index < target; index++)
        {
            var summary = _matches[index];
            Results.Add(new ModCardViewModel(
                summary,
                BuildBadge(summary),
                _installedIndex.Find(summary.ModId, summary.ModIdStrings),
                OpenAsync,
                StartInstallAsync,
                _logoCache));
        }

        HasMoreResults = Results.Count < ceiling;
        IsRenderCapped = Results.Count >= MaxRenderedResults && _matches.Count > Results.Count;
        ShownCountText = IsRenderCapped
            ? UiText.Mods.ShownCountCapped(Results.Count, _matches.Count)
            : UiText.Mods.ShownCount(Results.Count, _matches.Count);
    }

    // Sans index (aucune instance cible, ou ModDB muet), aucun badge : mieux vaut ne rien affirmer
    // qu'affirmer faux.
    private ModCompatibilityBadge BuildBadge(ModDbModSummary summary)
    {
        if (SelectedInstance?.GameVersion is not { } gameVersion || _compatibilityIndex is not { ModIds.Count: > 0 } index)
        {
            return ModCompatibilityBadge.None;
        }

        return ModCompatibilityBadge.For(gameVersion.ToString(), index.Contains(summary.ModId));
    }

    private async Task OpenAsync(ModCardViewModel card)
    {
        try
        {
            var detail = await _client.GetModAsync(
                card.ModId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CancellationToken.None).ConfigureAwait(true);

            // Le résumé d'une ligne vient de la CARTE, pas de la fiche : l'entrée de catalogue le
            // porte, la réponse de /api/mod/{id} non. Le passer ici évite d'aller le rechercher.
            _overlay.Show(new ModDetailDialogViewModel(
                detail,
                card.Description,
                SelectedInstance,
                _urlOpener,
                _overlay,
                _logoCache,
                () => StartInstallAsync(card)));
        }
        catch (Exception exception) when (exception is ModDbApiException or ModDbUnavailableException)
        {
            _toasts.Show(ToastTone.Error, UiText.Mods.DetailUnavailableTitle, card.Name);
        }
    }

    // Deux temps, comme le service : préparer (téléchargement + lecture du modinfo + plan), puis
    // appliquer ce que l'utilisateur a coché. Rien n'entre dans l'instance avant sa confirmation.
    private async Task StartInstallAsync(ModCardViewModel card)
    {
        if (SelectedInstance?.Slug is not { } slug)
        {
            _toasts.Show(ToastTone.Info, UiText.Mods.PickInstanceTitle, UiText.Mods.PickInstanceMessage);

            return;
        }

        card.IsBusy = true;
        IsInstalling = true;
        try
        {
            var plan = await PreparePlanAsync(slug, card.ModId, releaseId: null).ConfigureAwait(true);

            // Le dialogue reçoit de quoi RECALCULER un plan, pas seulement de quoi appliquer
            // celui-ci : changer de release dans le sélecteur repasse par la même préparation,
            // avec les mêmes dépendances lues dans la même archive téléchargée.
            _overlay.Show(new ModInstallPlanDialogViewModel(
                plan,
                SelectedInstance.Name,
                (chosen, selection) => ApplyAsync(slug, chosen, selection),
                releaseId => PreparePlanAsync(slug, card.ModId, releaseId),
                _overlay,
                _urlOpener,
                _logoCache,
                _logoDirectory));
        }
        catch (ModReleaseNotFoundException exception)
        {
            _toasts.Show(ToastTone.Error, UiText.Mods.NoCompatibleReleaseTitle, exception.Message);
        }
        catch (Exception exception) when (exception is ModDbApiException or ModDbUnavailableException or DownloadFailedException or ModInstallFailedException)
        {
            _toasts.Show(ToastTone.Error, UiText.Mods.InstallFailedTitle, exception.Message);
        }
        finally
        {
            card.IsBusy = false;
            IsInstalling = false;
            InstallProgress = null;
        }
    }

    private Task<ModInstallPlan> PreparePlanAsync(string slug, int modId, int? releaseId)
        => _installService.PrepareAsync(
            slug,
            modId,
            ModCompatibilityMode.ExactGameVersion,
            releaseId,
            new Progress<DownloadProgress>(OnProgress),
            CancellationToken.None);

    private async Task ApplyAsync(string slug, ModInstallPlan plan, IReadOnlyCollection<string> selection)
    {
        try
        {
            var outcome = await _installService
                .ApplyAsync(slug, plan, selection, new Progress<DownloadProgress>(OnProgress), CancellationToken.None)
                .ConfigureAwait(true);

            _overlay.Close();
            _toasts.Show(
                ToastTone.Success,
                UiText.Mods.InstalledTitle(plan.Primary.DisplayName),
                UiText.Mods.InstalledMessage(outcome.Installed.Count, SelectedInstance?.Name ?? slug));

            // Les cartes doivent refléter ce qui vient d'entrer dans l'instance, sans quitter
            // l'écran : rescan du dossier et remise à jour des états de chaque carte affichée.
            await RefreshInstalledStateAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is DownloadFailedException or ModInstallFailedException)
        {
            _overlay.Close();
            _toasts.Show(ToastTone.Error, UiText.Mods.InstallFailedTitle, exception.Message);
        }
        finally
        {
            InstallProgress = null;
        }
    }

    /// <summary>
    /// Rescanne l'instance ciblée et repose l'état de chaque carte DÉJÀ construite, sans reconstruire
    /// la fenêtre de rendu : rebâtir les cartes relancerait tous leurs chargements de logo et
    /// remonterait le défilement, pour un changement qui ne concerne qu'une propriété.
    /// </summary>
    private async Task RefreshInstalledStateAsync()
    {
        await LoadInstalledAsync(CancellationToken.None).ConfigureAwait(true);

        foreach (var card in Results)
        {
            card.Installed = _installedIndex.Find(card.ModId, card.Summary.ModIdStrings);
        }
    }

    private void OnProgress(DownloadProgress progress)
        => InstallProgress = progress.TotalBytes is > 0 ? (double)progress.ReceivedBytes / progress.TotalBytes.Value : null;
}

/// <summary>Un tag de catégorie affiché sous la barre de recherche.</summary>
public sealed partial class ModTagViewModel : ObservableObject
{
    /// <summary>Construit le tag.</summary>
    public ModTagViewModel(string name, bool isActive)
    {
        Name = name;
        _isActive = isActive;
    }

    /// <summary>Libellé de la catégorie, tel que l'expose <c>/api/tags</c>.</summary>
    public string Name { get; }

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// Une entrée du sélecteur d'instance cible. La première, « Toutes les versions », n'a pas de
/// version de jeu : elle désactive à la fois le filtre et les badges de compatibilité.
/// </summary>
public sealed class ModTargetInstanceViewModel
{
    /// <summary>Construit l'entrée pour une instance réelle.</summary>
    public ModTargetInstanceViewModel(string slug, string name, GameVersion gameVersion)
    {
        Slug = slug;
        Name = name;
        GameVersion = gameVersion;
        Label = UiText.Mods.InstanceLabel(name, gameVersion.ToString());
    }

    private ModTargetInstanceViewModel(string label)
    {
        Slug = null;
        Name = label;
        GameVersion = null;
        Label = label;
    }

    /// <summary>Entrée « toutes les versions », sans instance ni filtre.</summary>
    public static ModTargetInstanceViewModel AllVersions { get; } = new(UiText.Mods.AllVersions);

    /// <summary>Slug de l'instance, ou <see langword="null"/> pour l'entrée « toutes les versions ».</summary>
    public string? Slug { get; }

    /// <summary>Nom de l'instance.</summary>
    public string Name { get; }

    /// <summary>Version de jeu de l'instance, ou <see langword="null"/>.</summary>
    public GameVersion? GameVersion { get; }

    /// <summary>Libellé affiché dans le sélecteur.</summary>
    public string Label { get; }
}