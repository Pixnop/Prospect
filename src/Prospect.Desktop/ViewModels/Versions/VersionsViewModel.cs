using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Storage;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Versions;

/// <summary>
/// ViewModel de l'écran Versions (design/ui_kits/launcher/screen-versions.jsx) : filtre par canal
/// et par état, section des versions installées avec désinstallation, section des versions
/// disponibles au téléchargement, progression en ligne pendant une installation. Le scan local
/// passe par <see cref="IInstalledGameVersionRepository"/>, le catalogue par
/// <see cref="IGameVersionCatalog"/> et les mutations par <see cref="GameInstallService"/> : ce
/// ViewModel ne fait que les appeler.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Même raison qu'HomeViewModel, dont ce ViewModel reprend le sémaphore : VersionsViewModel est un " +
        "singleton pour toute la durée de l'application (voir CompositionRoot), et ShellViewModel.Navigate dispose la " +
        "page SORTANTE quand elle implémente IDisposable. Le rendre IDisposable couperait donc _loadGate dès la " +
        "première navigation quittant l'écran, cassant tout chargement ultérieur d'un écran qu'on revisite. WaitAsync " +
        "n'alloue jamais le AvailableWaitHandle sous-jacent, donc ce sémaphore ne détient aucune ressource système.")]
public sealed partial class VersionsViewModel : ObservableObject
{
    private readonly IGameVersionCatalog _catalog;
    private readonly IInstalledGameVersionRepository _repository;
    private readonly GameInstallService _installService;
    private readonly IInstanceRepository _instances;
    private readonly IOverlayService _overlay;
    private readonly IToastService _toasts;
    private readonly IUiDispatcher _dispatcher;
    private readonly AppOperatingSystem _operatingSystem;

    private IReadOnlyList<InstalledGameVersion> _installedVersions = [];
    private IReadOnlyList<GameVersionCatalogEntry> _catalogVersions = [];

    // Sérialise les chargements, comme le fait HomeViewModel pour ses scans. Ce n'était pas
    // nécessaire tant que seul un clic sur « Vérifier les nouveautés » pouvait en déclencher un ;
    // depuis que l'entrée sur la page en déclenche un toute seule, un second appel (retour sur
    // l'écran, fin d'installation, fin de désinstallation) peut très bien arriver pendant que le
    // premier tourne encore, et deux chargements vraiment concurrents entrelaceraient leurs
    // Clear()/Add() sur BrokenInstalls et leurs affectations de _installedVersions. La file
    // d'attente plutôt que la fusion : un appel qui arrive APRÈS une mutation (version tout juste
    // installée) doit voir un scan qui la trouve, pas le résultat d'un scan parti avant elle.
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public VersionsViewModel(
        IGameVersionCatalog catalog,
        IInstalledGameVersionRepository repository,
        GameInstallService installService,
        IInstanceRepository instances,
        AppPaths appPaths,
        IOverlayService overlay,
        IToastService toasts,
        IUiDispatcher dispatcher,
        IAppEnvironment appEnvironment)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(installService);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(appEnvironment);

        _catalog = catalog;
        _repository = repository;
        _installService = installService;
        _instances = instances;
        _overlay = overlay;
        _toasts = toasts;
        _dispatcher = dispatcher;
        _operatingSystem = appEnvironment.CurrentOperatingSystem;
        VersionsDirectoryText = appPaths.VersionsDirectory;
    }

    /// <summary>Versions présentes sur le disque, triées de la plus récente à la plus ancienne.</summary>
    public ObservableCollection<GameVersionRowViewModel> Installed { get; } = [];

    /// <summary>Versions du catalogue qui ne sont pas installées et dont un build existe pour cette machine.</summary>
    public ObservableCollection<GameVersionRowViewModel> Available { get; } = [];

    /// <summary>Dossiers de <c>versions/</c> qui ne sont pas des installations exploitables.</summary>
    public ObservableCollection<BrokenGameInstallRowViewModel> BrokenInstalls { get; } = [];

    /// <summary>Chemin du dossier partagé, affiché en monospace sous la liste comme dans la maquette.</summary>
    public string VersionsDirectoryText { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => Rebuild();

    /// <summary>0 = toutes, 1 = installées, 2 = disponibles. Pont vers le <c>ComboBox</c> de la vue.</summary>
    [ObservableProperty]
    private int _filterIndex;

    partial void OnFilterIndexChanged(int value) => Rebuild();

    /// <summary>Case « Afficher unstable et pre » de la maquette.</summary>
    [ObservableProperty]
    private bool _showUnstable = true;

    partial void OnShowUnstableChanged(bool value) => Rebuild();

    /// <summary>Sous-titre du PageHeader : nombre d'installations et place occupée.</summary>
    [ObservableProperty]
    private string _subtitleText = string.Empty;

    /// <summary>Message d'avertissement quand le catalogue vient d'un cache périmé ou n'a pas pu être joint.</summary>
    [ObservableProperty]
    private string? _catalogWarning;

    [ObservableProperty]
    private bool _showInstalledSection;

    [ObservableProperty]
    private bool _showAvailableSection;

    [ObservableProperty]
    private bool _hasBrokenInstalls;

    [ObservableProperty]
    private bool _showEmptyState;

    /// <summary>Relit le disque et consulte le catalogue, en se contentant du cache s'il est frais.</summary>
    [RelayCommand]
    private Task RefreshAsync(CancellationToken cancellationToken = default) => LoadAsync(forceRefresh: false, cancellationToken);

    /// <summary>« Vérifier les nouveautés » : ignore le cache et interroge l'API.</summary>
    [RelayCommand]
    private Task CheckForUpdatesAsync(CancellationToken cancellationToken = default) => LoadAsync(forceRefresh: true, cancellationToken);

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    private async Task LoadAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            await LoadCoreAsync(forceRefresh, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task LoadCoreAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var scan = await _repository.ScanAsync(cancellationToken).ConfigureAwait(true);
            _installedVersions = scan.Installed;

            BrokenInstalls.Clear();
            foreach (var broken in scan.Broken)
            {
                BrokenInstalls.Add(new BrokenGameInstallRowViewModel(broken));
            }

            HasBrokenInstalls = BrokenInstalls.Count > 0;
            await LoadCatalogAsync(forceRefresh, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
            Rebuild();
        }
    }

    private async Task LoadCatalogAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await _catalog.GetAsync(forceRefresh, cancellationToken).ConfigureAwait(true);
            _catalogVersions = catalog.Versions;
            CatalogWarning = catalog.Freshness == GameCatalogFreshness.Stale ? UiText.Versions.StaleCatalog : null;
        }
        catch (GameCatalogUnavailableException)
        {
            // Le catalogue est un confort : sans lui, l'écran montre encore ce qui est installé.
            _catalogVersions = [];
            CatalogWarning = UiText.Versions.UnavailableCatalog;
        }
    }

    private void Rebuild()
    {
        var installedVersions = _installedVersions.Select(entry => entry.Version).ToHashSet();

        Installed.Clear();
        if (FilterIndex != 2)
        {
            foreach (var entry in _installedVersions.Where(entry => Matches(entry.Version.Channel, entry.Version.ToString())))
            {
                Installed.Add(CreateInstalledRow(entry));
            }
        }

        Available.Clear();
        if (FilterIndex != 1)
        {
            foreach (var entry in _catalogVersions.Where(entry =>
                         !installedVersions.Contains(entry.Version) && Matches(entry.Channel, entry.Version.ToString())))
            {
                var asset = _installService.FindInstallableAsset(entry);
                if (asset is not null)
                {
                    Available.Add(CreateAvailableRow(entry, asset));
                }
            }
        }

        ShowInstalledSection = Installed.Count > 0;
        ShowAvailableSection = Available.Count > 0;
        ShowEmptyState = !IsLoading && Installed.Count == 0 && Available.Count == 0;
        SubtitleText = UiText.Versions.Subtitle(
            _installedVersions.Count,
            ByteSizeFormatter.Format(_installedVersions.Sum(entry => entry.SizeBytes)));
    }

    private bool Matches(GameVersionChannel channel, string versionText)
    {
        if (!ShowUnstable && channel != GameVersionChannel.Stable)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(SearchText)
            || versionText.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private GameVersionRowViewModel CreateInstalledRow(InstalledGameVersion entry)
        => new(
            entry.Version,
            ByteSizeFormatter.Format(entry.SizeBytes),
            UiText.Versions.InstalledOn(entry.InstalledUtc),
            isInstalled: true,
            _installService,
            _dispatcher,
            _operatingSystem,
            OnInstalledAsync,
            RequestUninstallAsync);

    private GameVersionRowViewModel CreateAvailableRow(GameVersionCatalogEntry entry, GameVersionAsset asset)
        => new(
            entry.Version,
            asset.DisplaySize,
            asset.FileName,
            isInstalled: false,
            _installService,
            _dispatcher,
            _operatingSystem,
            OnInstalledAsync,
            RequestUninstallAsync);

    private async Task OnInstalledAsync(GameVersionRowViewModel row)
    {
        _toasts.Show(ToastTone.Success, UiText.Toasts.VersionInstalledTitle, row.VersionText);
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
    }

    // La confirmation nomme les instances concernées plutôt que de servir un avertissement
    // générique : « Homestead 1.21 et Bac à sable utilisent cette version » est actionnable,
    // « des instances utilisent peut-être cette version » ne l'est pas.
    private async Task RequestUninstallAsync(GameVersionRowViewModel row)
    {
        var scan = await _instances.ScanAsync(CancellationToken.None).ConfigureAwait(true);
        var dependents = scan.Instances
            .Where(instance => instance.Metadata.GameVersion == row.Version)
            .Select(instance => instance.Metadata.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _overlay.Show(new UninstallVersionDialogViewModel(
            row.VersionText,
            dependents,
            progress => ConfirmUninstallAsync(row, progress),
            _overlay,
            _dispatcher));
    }

    // Le dialogue reste ouvert le temps de la suppression et suit son avancement ; il ne se referme
    // qu'une fois le dossier réellement parti. Un échec partiel remonte au dialogue, qui le dit,
    // plutôt que d'être avalé ici.
    private async Task ConfirmUninstallAsync(GameVersionRowViewModel row, IProgress<DirectoryDeleteProgress> progress)
    {
        await _installService.UninstallAsync(row.Version, progress, CancellationToken.None).ConfigureAwait(true);

        _overlay.Close();
        _toasts.Show(ToastTone.Info, UiText.Toasts.VersionUninstalledTitle, row.VersionText);
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
    }
}

/// <summary>
/// Un dossier de <c>versions/</c> qui n'a pas pu être compté comme installation, affiché dans la
/// zone discrète du bas de l'écran, comme les instances cassées sur l'Accueil.
/// </summary>
public sealed class BrokenGameInstallRowViewModel
{
    public BrokenGameInstallRowViewModel(BrokenGameInstall broken)
    {
        ArgumentNullException.ThrowIfNull(broken);

        FolderName = broken.FolderName;
        Reason = UiText.Versions.BrokenReason(broken.Reason);
    }

    public string FolderName { get; }

    public string Reason { get; }
}