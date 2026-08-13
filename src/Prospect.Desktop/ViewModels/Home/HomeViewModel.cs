using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Launching;
using Prospect.Core.Migration;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.FirstRun;
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
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "HomeViewModel est un singleton pour toute la durée de l'application (voir CompositionRoot). " +
        "Le rendre IDisposable serait dangereux : ShellViewModel.Navigate dispose systématiquement la page SORTANTE " +
        "quand elle implémente IDisposable, et Home redevient page sortante à chaque navigation vers Réglages/Mods/" +
        "Versions/détail d'instance. Le sémaphore de _refreshGate serait alors coupé dès la première navigation, " +
        "cassant tout rafraîchissement ultérieur d'un Accueil qu'on revisite ensuite. WaitAsync n'alloue jamais le " +
        "AvailableWaitHandle sous-jacent (seuls Wait() ou la lecture de cette propriété le feraient), donc ce " +
        "sémaphore ne détient en pratique aucune ressource système à libérer.")]
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly InstanceService _instanceService;
    private readonly IInstanceRepository _repository;
    private readonly GameLauncher _launcher;
    private readonly RunningInstanceTracker _tracker;
    private readonly IClock _clock;
    private readonly IModUpdateCheckCache _updateCache;
    private readonly IOverlayService _overlay;
    private readonly IToastService _toasts;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<WizardViewModel> _wizardFactory;
    private readonly List<InstanceCardViewModel> _allInstances = [];
    private readonly NewInstanceTileViewModel _newInstanceTile;

    // Sérialise les scans (voir RefreshAsync) : jamais disposé, HomeViewModel est un singleton
    // qui vit toute la durée de l'application (voir CompositionRoot) et n'implémente pas
    // IDisposable — l'ajouter ferait disposer ce sémaphore par ShellViewModel.Navigate() au
    // premier changement de page, cassant tout rafraîchissement ultérieur d'un Accueil qu'on
    // revisite. WaitAsync n'alloue jamais le AvailableWaitHandle sous-jacent (seuls Wait() ou la
    // lecture de cette propriété le feraient), donc il n'y a rien à libérer côté OS en pratique.
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public HomeViewModel(
        InstanceService instanceService,
        IInstanceRepository repository,
        GameLauncher launcher,
        RunningInstanceTracker tracker,
        IClock clock,
        IModUpdateCheckCache updateCache,
        IOverlayService overlay,
        IToastService toasts,
        IUiDispatcher dispatcher,
        Func<WizardViewModel> wizardFactory,
        FirstRunViewModel firstRun)
    {
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(updateCache);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(wizardFactory);
        ArgumentNullException.ThrowIfNull(firstRun);

        _instanceService = instanceService;
        _repository = repository;
        _launcher = launcher;
        _tracker = tracker;
        _clock = clock;
        _updateCache = updateCache;
        _overlay = overlay;
        _toasts = toasts;
        _dispatcher = dispatcher;
        _wizardFactory = wizardFactory;
        _newInstanceTile = new NewInstanceTileViewModel(NewInstance);

        FirstRun = firstRun;
        FirstRun.Completed += OnVslAdopted;
    }

    /// <summary>Levé quand une carte est ouverte (clic hors bouton Jouer/Arrêter et menu) : la page de détail correspondante.</summary>
    public event EventHandler<string>? InstanceOpenRequested;

    /// <summary>
    /// Rappel de premier lancement affiché par la vue dans l'état vide (voir
    /// <see cref="HasNoInstancesAtAll"/>) : la carte d'adoption VS Launcher ne s'affiche que si
    /// <see cref="ViewModels.FirstRun.FirstRunViewModel.VslDetected"/> est vrai, elle-même mise à
    /// jour par <see cref="RefreshAsync"/> tant que la bibliothèque est vide (voir plus bas).
    /// </summary>
    public FirstRunViewModel FirstRun { get; }

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

    /// <summary>
    /// Point d'entrée unique du rafraîchissement. Sérialise les scans via <see cref="_refreshGate"/>
    /// plutôt que de les laisser se chevaucher : depuis que <see cref="ViewModels.Shell.ShellViewModel"/>
    /// déclenche ce rafraîchissement tout seul au démarrage (l'angle mort corrigé — voir sa
    /// docstring), un second appel (création d'instance, adoption VS Launcher)
    /// peut très bien arriver pendant que ce premier scan tourne encore. Deux scans VRAIMENT
    /// concurrents entrelaceraient leurs <c>Clear()</c>/<c>AddRange()</c> sur <see cref="_allInstances"/>
    /// (et disposeraient les mêmes cartes deux fois) — mais fusionner le second appel dans le
    /// premier (au lieu de le mettre en file) serait FAUX : un appel qui arrive après une mutation
    /// (nouvelle instance créée, par exemple) doit voir un scan qui la trouve, pas le résultat d'un
    /// scan parti avant qu'elle n'existe. La file d'attente (un seul scan à la fois, mais chaque
    /// appelant obtient bien SON propre scan, exécuté après celui qui le précède) est le seul
    /// choix qui satisfait les deux contraintes à la fois.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var result = await _repository.ScanAsync(cancellationToken).ConfigureAwait(true);
            var now = _clock.UtcNow;

            // Chaque carte s'abonne à RunningInstanceTracker.StatusChanged (un singleton qui vit
            // toute la durée de l'application) : sans ce Dispose, les cartes remplacées à chaque
            // rafraîchissement s'accumuleraient indéfiniment côté tracker (voir la docstring de
            // InstanceCardViewModel.Dispose).
            foreach (var previous in _allInstances)
            {
                previous.Dispose();
            }

            _allInstances.Clear();
            _allInstances.AddRange(result.Instances.Select(record =>
            {
                var card = new InstanceCardViewModel(
                    record,
                    RelativeDateFormatter.Format(record.Metadata.LastLaunchedUtc, now),
                    _instanceService,
                    _launcher,
                    _tracker,
                    _updateCache,
                    _overlay,
                    _toasts,
                    _dispatcher,
                    () => RefreshAsync(CancellationToken.None));
                card.OpenRequested += (_, slug) => InstanceOpenRequested?.Invoke(this, slug);

                return card;
            }));

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

        // Sans intérêt une fois la bibliothèque peuplée (la carte ne s'affiche plus dans ce cas de
        // toute façon, voir HomeView) : inutile de relancer une détection VS Launcher à chaque
        // rafraîchissement d'une Accueil déjà habitée.
        if (HasNoInstancesAtAll)
        {
            await FirstRun.InitializeCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    // Le wizard vient d'une fabrique du conteneur plutôt que d'un « new » : depuis qu'il consomme
    // le catalogue de versions, il a plus de dépendances que l'Accueil n'en a lui-même, et les lui
    // faire traverser n'apprendrait rien à personne.
    [RelayCommand]
    private void NewInstance()
    {
        var wizard = _wizardFactory();
        wizard.Created += OnInstanceCreated;
        _overlay.Show(wizard);
        _ = wizard.LoadVersionsCommand.ExecuteAsync(null);
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
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        _toasts.Show(
            ToastTone.Success,
            UiText.Toasts.InstanceCreatedTitle,
            UiText.Toasts.WithVersion(record.Metadata.Name, record.Metadata.GameVersion.ToString()));
    }

    // Le toast de fin d'adoption est déjà affiché par AdoptVslViewModel lui-même (le service fait,
    // le ViewModel raconte) : ce gestionnaire ne fait que
    // rafraîchir, pour que les instances tout juste créées apparaissent sans action supplémentaire.
    private void OnVslAdopted(object? sender, VslAdoptionOutcome outcome) => _ = RefreshAsync(CancellationToken.None);

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