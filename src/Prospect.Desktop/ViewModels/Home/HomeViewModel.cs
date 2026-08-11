using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;
using Prospect.Desktop.ViewModels.Wizard;

namespace Prospect.Desktop.ViewModels.Home;

/// <summary>
/// ViewModel de l'écran Accueil (design/ui_kits/launcher/screen-home.jsx) : grille d'instances,
/// recherche plein texte sur le nom, tri nom/dernier lancement, états vides, zone discrète des
/// instances cassées. Le scan (lecture) passe par <see cref="IInstanceRepository"/>, les mutations
/// par <see cref="InstanceService"/> — la même répartition que le reste du domaine Instances (voir
/// docs/architecture.md, patterns Repository / Services applicatifs) : ce ViewModel ne fait que
/// les appeler, jamais de logique métier composée à partir de briques plus basses.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly InstanceService _instanceService;
    private readonly IInstanceRepository _repository;
    private readonly IClock _clock;
    private readonly IOverlayService _overlay;
    private readonly IToastService _toasts;
    private readonly List<InstanceCardViewModel> _allInstances = [];
    private readonly NewInstanceTileViewModel _newInstanceTile;

    public HomeViewModel(
        InstanceService instanceService,
        IInstanceRepository repository,
        IClock clock,
        IOverlayService overlay,
        IToastService toasts)
    {
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);

        _instanceService = instanceService;
        _repository = repository;
        _clock = clock;
        _overlay = overlay;
        _toasts = toasts;
        _newInstanceTile = new NewInstanceTileViewModel(NewInstance);
    }

    public ObservableCollection<InstanceCardViewModel> Instances { get; } = new();

    /// <summary>
    /// Instances filtrées/triées suivies de la tuile « nouvelle instance » (voir
    /// <see cref="NewInstanceTileViewModel"/>) : la collection qu'affiche réellement la grille, un
    /// seul <c>ItemsControl</c> à panneau <c>WrapPanel</c> pour que la tuile flotte avec les cartes.
    /// Vide quand un état vide est affiché à la place (voir <see cref="HasNoInstancesAtAll"/> et
    /// <see cref="ShowSearchEmptyState"/>).
    /// </summary>
    public ObservableCollection<object> GridItems { get; } = new();

    public ObservableCollection<BrokenInstanceRowViewModel> BrokenInstances { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoInstancesAtAll))]
    [NotifyPropertyChangedFor(nameof(ShowSearchEmptyState))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoResultsDescription))]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilterAndSort();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortModeIndex))]
    private HomeSortMode _sortMode = HomeSortMode.LastLaunched;

    partial void OnSortModeChanged(HomeSortMode value) => ApplyFilterAndSort();

    /// <summary>Pont entre <see cref="SortMode"/> et l'index sélectionné du ComboBox de tri de la vue.</summary>
    public int SortModeIndex
    {
        get => SortMode == HomeSortMode.Name ? 1 : 0;
        set => SortMode = value == 1 ? HomeSortMode.Name : HomeSortMode.LastLaunched;
    }

    /// <summary>Aucune instance n'existe, avant même filtrage : état vide de première utilisation.</summary>
    public bool HasNoInstancesAtAll => !IsLoading && _allInstances.Count == 0;

    /// <summary>Des instances existent, mais aucune ne correspond au filtre courant.</summary>
    public bool ShowSearchEmptyState => !IsLoading && _allInstances.Count > 0 && Instances.Count == 0;

    public bool HasBrokenInstances => BrokenInstances.Count > 0;

    public string NoResultsDescription => UiText.Home.NoSearchResults(SearchText);

    /// <summary>Sous-titre du PageHeader (design : « N instances »), vide tant qu'aucune instance n'existe.</summary>
    public string SubtitleText => _allInstances.Count switch
    {
        0 => string.Empty,
        1 => "1 instance",
        var count => $"{count} instances",
    };

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var result = await _repository.ScanAsync(cancellationToken).ConfigureAwait(true);
            var now = _clock.UtcNow;

            _allInstances.Clear();
            _allInstances.AddRange(result.Instances.Select(record => new InstanceCardViewModel(
                record,
                RelativeDateFormatter.Format(record.Metadata.LastLaunchedUtc, now),
                _instanceService,
                _overlay,
                () => RefreshAsync())));

            BrokenInstances.Clear();
            foreach (var broken in result.BrokenInstances)
            {
                BrokenInstances.Add(new BrokenInstanceRowViewModel(broken));
            }

            OnPropertyChanged(nameof(HasBrokenInstances));
            OnPropertyChanged(nameof(SubtitleText));
        }
        finally
        {
            IsLoading = false;
            ApplyFilterAndSort();
        }
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void NewInstance()
    {
        var wizard = new WizardViewModel(_instanceService, _overlay);
        wizard.Created += OnInstanceCreated;
        _overlay.Show(wizard);
    }

    // Gestionnaire volontairement synchrone (pas async void, dangereux : une exception y
    // échapperait à tout appelant) : le travail asynchrone réel vit dans HandleInstanceCreatedAsync,
    // appelée ici en tâche de fond.
    private void OnInstanceCreated(object? sender, InstanceRecord record)
    {
        if (sender is WizardViewModel wizard)
        {
            wizard.Created -= OnInstanceCreated;
        }

        _ = HandleInstanceCreatedAsync(record);
    }

    private async Task HandleInstanceCreatedAsync(InstanceRecord record)
    {
        await RefreshAsync().ConfigureAwait(true);
        _toasts.Show(
            ToastTone.Success,
            UiText.Toasts.InstanceCreatedTitle,
            UiText.Toasts.WithVersion(record.Metadata.Name, record.Metadata.GameVersion.ToString()));
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<InstanceCardViewModel> filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allInstances
            : _allInstances.Where(instance => instance.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        var sorted = SortMode switch
        {
            HomeSortMode.Name => filtered.OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase),
            _ => filtered
                .OrderByDescending(instance => instance.LastLaunchedUtc ?? DateTimeOffset.MinValue)
                .ThenBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase),
        };

        Instances.Clear();
        foreach (var instance in sorted)
        {
            Instances.Add(instance);
        }

        GridItems.Clear();
        if (Instances.Count > 0)
        {
            foreach (var instance in Instances)
            {
                GridItems.Add(instance);
            }

            GridItems.Add(_newInstanceTile);
        }

        OnPropertyChanged(nameof(HasNoInstancesAtAll));
        OnPropertyChanged(nameof(ShowSearchEmptyState));
    }
}