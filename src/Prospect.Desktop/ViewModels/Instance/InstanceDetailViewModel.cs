using System.Collections.ObjectModel;
using System.IO.Abstractions;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Launching;
using Prospect.Core.Storage;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Common;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Instance;

/// <summary>
/// ViewModel de la page de détail d'une instance (design/ui_kits/launcher/screen-instance.jsx) :
/// en-tête (icône, nom, version badgée, Jouer/Arrêter, menu renommer/dupliquer/supprimer) et
/// quatre onglets (Mods en attente de la PR suivante, Mondes, Journal, Options). Transient (une
/// instance par instance ouverte, voir <c>Func&lt;string, InstanceDetailViewModel&gt;</c> dans
/// <see cref="Prospect.Desktop.CompositionRoot"/>), à la différence des pages de la sidebar qui
/// sont des singletons.
/// </summary>
public sealed partial class InstanceDetailViewModel : ObservableObject, IDisposable
{
    private readonly string _slug;
    private readonly InstanceService _instanceService;
    private readonly IInstanceRepository _repository;
    private readonly GameLauncher _launcher;
    private readonly RunningInstanceTracker _tracker;
    private readonly IAppEnvironment _appEnvironment;
    private readonly IFileSystem _fileSystem;
    private readonly IOverlayService _overlay;
    private readonly IToastService _toasts;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClock _clock;

    public InstanceDetailViewModel(
        string slug,
        InstanceService instanceService,
        IInstanceRepository repository,
        GameLauncher launcher,
        RunningInstanceTracker tracker,
        IAppEnvironment appEnvironment,
        IFileSystem fileSystem,
        IOverlayService overlay,
        IToastService toasts,
        IUiDispatcher dispatcher,
        IClock clock)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(appEnvironment);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(clock);

        _slug = slug;
        _instanceService = instanceService;
        _repository = repository;
        _launcher = launcher;
        _tracker = tracker;
        _appEnvironment = appEnvironment;
        _fileSystem = fileSystem;
        _overlay = overlay;
        _toasts = toasts;
        _dispatcher = dispatcher;
        _clock = clock;

        Slug = slug;
        PathText = repository.GetDataDirectory(slug);

        // Valeurs sobres avant le premier chargement (voir InitializeAsync, invoqué par
        // ShellViewModel juste après construction, même pattern que WizardViewModel.LoadVersionsCommand) :
        // la vue affiche déjà une coquille cohérente plutôt que des champs vides le temps du
        // premier accès disque.
        _name = slug;
        _versionText = string.Empty;
        _channelBadgeTone = string.Empty;
        _iconKey = "layers";
        _playtimeText = PlaytimeFormatter.Format(0);
        _lastPlayedText = string.Empty;
        _optionsTab = new InstanceOptionsTabViewModel(slug, InstanceLaunchSettings.Empty, instanceService, appEnvironment, toasts);
        _isRunning = tracker.IsRunning(slug);

        ModsPlaceholder = new PlaceholderPageViewModel(
            "package",
            "Mods",
            "Bientôt disponible : installation et gestion des mods de cette instance depuis le ModDB officiel.");

        _tracker.StatusChanged += OnTrackerStatusChanged;
    }

    /// <summary>Demande le retour à l'Accueil (bouton « ← Instances », ou instance supprimée).</summary>
    public event EventHandler? BackRequested;

    /// <summary>Demande la navigation vers l'écran Versions (action « Installer » du bandeau d'erreur).</summary>
    public event EventHandler? NavigateToVersionsRequested;

    public string Slug { get; }

    /// <summary>Chemin absolu de <c>data/</c>, affiché en monospace dans la ligne meta de l'en-tête.</summary>
    public string PathText { get; }

    public PlaceholderPageViewModel ModsPlaceholder { get; }

    public ObservableCollection<InstanceWorldRowViewModel> Worlds { get; } = [];

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _versionText;

    [ObservableProperty]
    private string _channelBadgeTone;

    [ObservableProperty]
    private string _iconKey;

    [ObservableProperty]
    private string _playtimeText;

    [ObservableProperty]
    private string _lastPlayedText;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasWorlds;

    [ObservableProperty]
    private string _journalContent = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJournalContent))]
    private bool _hasEverLaunched;

    public bool HasJournalContent => HasEverLaunched && JournalContent.Length > 0;

    [ObservableProperty]
    private InstanceOptionsTabViewModel _optionsTab;

    // Mods en premier, comme la maquette (useState('Mods')) : la page garde le même ordre et la
    // même sélection par défaut que design/ui_kits/launcher/screen-instance.jsx, même si Mods
    // n'est qu'un placeholder pour cette PR.
    [ObservableProperty]
    private InstanceDetailTab _selectedTab = InstanceDetailTab.Mods;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestStopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool _isLaunching;

    [ObservableProperty]
    private string? _launchErrorTitle;

    [ObservableProperty]
    private string? _launchErrorMessage;

    [ObservableProperty]
    private bool _showInstallAction;

    public bool ShowLaunchError => LaunchErrorMessage is not null;

    [RelayCommand]
    private async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var record = await _repository.LoadAsync(_slug, cancellationToken).ConfigureAwait(true);
            ApplyRecord(record);
            OptionsTab = new InstanceOptionsTabViewModel(_slug, record.Metadata.Launch, _instanceService, _appEnvironment, _toasts);

            var worlds = await _repository.ListWorldsAsync(_slug, cancellationToken).ConfigureAwait(true);
            Worlds.Clear();
            foreach (var world in worlds.OrderByDescending(file => file.LastModifiedUtc))
            {
                Worlds.Add(new InstanceWorldRowViewModel(world, _clock.UtcNow));
            }

            HasWorlds = Worlds.Count > 0;

            RefreshJournal();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectTab(InstanceDetailTab tab) => SelectedTab = tab;

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void GoToVersions() => NavigateToVersionsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void RefreshJournal()
    {
        var logPath = _launcher.GetLogFilePath(_slug);
        var exists = _fileSystem.File.Exists(logPath);
        JournalContent = exists ? _fileSystem.File.ReadAllText(logPath) : string.Empty;
        HasEverLaunched = exists;
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        IsLaunching = true;
        ClearLaunchError();
        try
        {
            await _launcher.LaunchAsync(_slug, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InstanceNotFoundException or InstanceAlreadyRunningException
                                        or GameVersionNotInstalledException or Core.Runtime.RuntimeNotAvailableException
                                        or MacLaunchNotSupportedException)
        {
            var presentation = LaunchErrorPresenter.Describe(ex);
            LaunchErrorTitle = presentation.Title;
            LaunchErrorMessage = presentation.Message;
            ShowInstallAction = presentation.Action == LaunchErrorAction.InstallVersion;
        }
        finally
        {
            IsLaunching = false;
        }
    }

    private bool CanPlay() => !IsRunning && !IsLaunching && !IsLoading;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void RequestStop()
        => _overlay.Show(new StopInstanceDialogViewModel(Name, () => _tracker.RequestStop(_slug), _overlay));

    [RelayCommand]
    private void Rename() => _overlay.Show(new RenameDialogViewModel(_slug, Name, _instanceService, _overlay, ReloadHeaderAsync));

    [RelayCommand]
    private void Duplicate() => _overlay.Show(new DuplicateDialogViewModel(_slug, Name, _instanceService, _overlay, OnDuplicatedAsync));

    [RelayCommand]
    private void Delete() => _overlay.Show(new DeleteInstanceDialogViewModel(_slug, Name, _instanceService, _overlay, OnDeletedAsync));

    // Contrairement à Rename (dont le résultat se voit déjà dans l'en-tête rechargé), une
    // duplication ne change rien à la page affichée : la seule instance concernée par ce
    // ViewModel est la source, pas la copie. Un toast est ici le seul retour visible possible,
    // à la différence de l'Accueil où la nouvelle carte apparaît directement dans la grille.
    private Task OnDuplicatedAsync()
    {
        _toasts.Show(ToastTone.Success, UiText.Toasts.InstanceDuplicatedTitle);

        return Task.CompletedTask;
    }

    private Task OnDeletedAsync()
    {
        BackRequested?.Invoke(this, EventArgs.Empty);

        return Task.CompletedTask;
    }

    private async Task ReloadHeaderAsync()
    {
        var record = await _repository.LoadAsync(_slug, CancellationToken.None).ConfigureAwait(true);
        ApplyRecord(record);
    }

    private void ApplyRecord(InstanceRecord record)
    {
        Name = record.Metadata.Name;
        VersionText = record.Metadata.GameVersion.ToString();
        ChannelBadgeTone = ChannelBadgePresentation.ToBadgeTone(record.Metadata.GameVersion.Channel);
        IconKey = InstanceIconKeyResolver.Resolve(record.Metadata.Icon);
        PlaytimeText = PlaytimeFormatter.Format(record.Metadata.TotalPlaytimeSeconds);
        LastPlayedText = RelativeDateFormatter.Format(record.Metadata.LastLaunchedUtc, _clock.UtcNow);
    }

    private void ClearLaunchError()
    {
        LaunchErrorTitle = null;
        LaunchErrorMessage = null;
        ShowInstallAction = false;
    }

    private void OnTrackerStatusChanged(object? sender, RunningInstanceStatus status)
    {
        if (!string.Equals(status.Slug, _slug, StringComparison.Ordinal))
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            IsRunning = status.State == RunningInstanceState.Started;
            if (status.State == RunningInstanceState.Stopped)
            {
                _ = ReloadHeaderAsync();
            }
        });
    }

    partial void OnLaunchErrorMessageChanged(string? value) => OnPropertyChanged(nameof(ShowLaunchError));

    /// <summary>Se désabonne de <see cref="RunningInstanceTracker"/> (voir <see cref="Home.InstanceCardViewModel.Dispose"/> pour la même raison).</summary>
    public void Dispose() => _tracker.StatusChanged -= OnTrackerStatusChanged;
}