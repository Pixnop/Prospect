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
/// La recherche et le tri se font ENTIÈREMENT en mémoire (<see cref="ModCatalogSearch"/>) : l'API
/// ne pagine rien, le catalogue entier arrive en un appel et repart à chaque frappe serait à la
/// fois plus lent et impoli. Seul l'index de compatibilité vient du serveur, qui est le seul à
/// savoir quelle release est taguée pour quelle version de jeu.
/// </remarks>
public sealed partial class ModBrowserViewModel : ObservableObject
{
    private readonly IModDbClient _client;
    private readonly ModInstallService _installService;
    private readonly IInstanceRepository _instances;
    private readonly IExternalUrlOpener _urlOpener;
    private readonly IOverlayService _overlay;
    private readonly IToastService _toasts;
    private readonly IModLogoCache _logoCache;

    private IReadOnlyList<ModDbModSummary> _catalog = [];
    private ModDbCompatibilityIndex? _compatibilityIndex;

    public ModBrowserViewModel(
        IModDbClient client,
        ModInstallService installService,
        IInstanceRepository instances,
        IExternalUrlOpener urlOpener,
        IOverlayService overlay,
        IToastService toasts,
        IModLogoCache logoCache)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(installService);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(urlOpener);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(logoCache);

        _client = client;
        _installService = installService;
        _instances = instances;
        _urlOpener = urlOpener;
        _overlay = overlay;
        _toasts = toasts;
        _logoCache = logoCache;
    }

    /// <summary>Résultats affichés dans la grille.</summary>
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
            foreach (var tag in tags.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                Tags.Add(new ModTagViewModel(tag.Name, string.Equals(tag.Name, ActiveTagName, StringComparison.Ordinal)));
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

    partial void OnSelectedInstanceChanged(ModTargetInstanceViewModel? value)
        => _ = ReloadCompatibilityAsync();

    private async Task ReloadCompatibilityAsync()
    {
        await LoadCompatibilityAsync(CancellationToken.None).ConfigureAwait(true);
        Rebuild();
    }

    private void Rebuild()
    {
        var query = new ModCatalogQuery(
            SearchText,
            ActiveTagName,
            (ModCatalogSort)SortIndex,
            SelectedInstance?.GameVersion is null ? ModCompatibilityFilter.All : ModCompatibilityFilter.CompatibleOnly);

        var matches = ModCatalogSearch.Apply(_catalog, query, _compatibilityIndex);

        // Chaque carte peut avoir un chargement de logo en vol (IModLogoCache) : sans ce Dispose,
        // reconstruire Results à chaque frappe de recherche laisserait une traînée de
        // téléchargements pour des cartes déjà jetées (même mécanique que HomeViewModel.RefreshAsync
        // pour InstanceCardViewModel).
        foreach (var previous in Results)
        {
            previous.Dispose();
        }

        Results.Clear();
        foreach (var summary in matches)
        {
            Results.Add(new ModCardViewModel(summary, BuildBadge(summary), OpenAsync, StartInstallAsync, _logoCache));
        }

        SubtitleText = UiText.Mods.Subtitle(_catalog.Count);
        ShowEmptyState = !IsLoading && Results.Count == 0;
        EmptyStateTitle = IsOffline ? UiText.Mods.OfflineEmptyTitle : UiText.Mods.EmptyResultsTitle;
        EmptyStateDescription = IsOffline
            ? UiText.Mods.OfflineEmptyDescription
            : UiText.Mods.EmptyResultsDescription(SearchText);
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

            _overlay.Show(new ModDetailDialogViewModel(
                detail,
                SelectedInstance,
                _urlOpener,
                _overlay,
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
            var plan = await _installService
                .PrepareAsync(slug, card.ModId, ModCompatibilityMode.ExactGameVersion, new Progress<DownloadProgress>(OnProgress), CancellationToken.None)
                .ConfigureAwait(true);

            _overlay.Show(new ModInstallPlanDialogViewModel(
                plan,
                SelectedInstance.Name,
                selection => ApplyAsync(slug, plan, selection),
                _overlay));
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