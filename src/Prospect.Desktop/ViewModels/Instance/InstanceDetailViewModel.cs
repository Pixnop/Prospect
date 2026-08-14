using System.Collections.ObjectModel;
using System.IO.Abstractions;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Backups;
using Prospect.Core.Common;
using Prospect.Core.Diagnostics;
using Prospect.Core.Instances;
using Prospect.Core.Launching;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Instance;

/// <summary>
/// ViewModel de la page de détail d'une instance (design/ui_kits/launcher/screen-instance.jsx) :
/// en-tête (icône, nom, version badgée, Jouer/Arrêter, menu renommer/dupliquer/supprimer) et
/// quatre onglets (Mods, Mondes, Journal, Options). Transient (une instance par instance ouverte,
/// voir <c>Func&lt;string, InstanceDetailViewModel&gt;</c> dans
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
    private readonly InstanceBackupService _backupService;
    private readonly IModUpdateCheckCache _updateCache;
    private readonly InstanceDoctor _doctor;
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
        InstanceBackupService backupService,
        IInstalledModRepository mods,
        ModInstallService modInstallService,
        ModUpdateChecker updateChecker,
        IModUpdateCheckCache updateCache,
        GameLogInsightsService logInsights,
        IGameLogInsightsCache logInsightsCache,
        InstanceDoctor doctor,
        IAppEnvironment appEnvironment,
        IFileSystem fileSystem,
        IOverlayService overlay,
        IToastService toasts,
        IUiDispatcher dispatcher,
        IClock clock,
        IExternalUrlOpener urlOpener,
        IModLogoCache logoCache)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(backupService);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(modInstallService);
        ArgumentNullException.ThrowIfNull(updateChecker);
        ArgumentNullException.ThrowIfNull(updateCache);
        ArgumentNullException.ThrowIfNull(logInsights);
        ArgumentNullException.ThrowIfNull(logInsightsCache);
        ArgumentNullException.ThrowIfNull(doctor);
        ArgumentNullException.ThrowIfNull(appEnvironment);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(urlOpener);
        ArgumentNullException.ThrowIfNull(logoCache);

        _slug = slug;
        _instanceService = instanceService;
        _repository = repository;
        _launcher = launcher;
        _tracker = tracker;
        _backupService = backupService;
        _updateCache = updateCache;
        _doctor = doctor;
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
        _optionsTab = new InstanceOptionsTabViewModel(
            slug, slug, InstanceLaunchSettings.Empty, InstanceBackupSettings.Default,
            instanceService, backupService, appEnvironment, overlay, clock, toasts);
        _isRunning = tracker.IsRunning(slug);

        // Le chemin du journal est résolu une fois, ici : c'est le domaine du lancement qui sait
        // où il vit (docs/architecture.md, section « 3. Lancement »), et l'onglet Mods n'a besoin
        // que de le LIRE — lui passer le launcher entier lui donnerait de quoi lancer le jeu.
        ModsTab = new InstanceModsTabViewModel(
            slug, mods, modInstallService, updateChecker, updateCache,
            logInsights, logInsightsCache, launcher.GetLogFilePath(slug),
            clock, overlay, toasts, urlOpener, logoCache);
        ModsTab.BrowseRequested += (_, instanceSlug) => BrowseModsRequested?.Invoke(this, instanceSlug);

        _tracker.StatusChanged += OnTrackerStatusChanged;
    }

    /// <summary>Demande le retour à l'Accueil (bouton « ← Instances », ou instance supprimée).</summary>
    public event EventHandler? BackRequested;

    /// <summary>Demande la navigation vers l'écran Versions (action « Installer » du bandeau d'erreur).</summary>
    public event EventHandler? NavigateToVersionsRequested;

    /// <summary>Demande l'ouverture du navigateur de mods, préfiltré sur cette instance.</summary>
    public event EventHandler<string>? BrowseModsRequested;

    public string Slug { get; }

    /// <summary>Chemin absolu de <c>data/</c>, affiché en monospace dans la ligne meta de l'en-tête.</summary>
    public string PathText { get; }

    /// <summary>Onglet Mods : liste installée, activation, désinstallation.</summary>
    public InstanceModsTabViewModel ModsTab { get; }

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

    /// <summary>« Sauvegarde en cours (3/12)… », rempli seulement pendant la sauvegarde automatique de pré-lancement (voir <see cref="PlayAsync"/>), vide sinon.</summary>
    [ObservableProperty]
    private string _autoBackupProgressText = string.Empty;

    public bool ShowLaunchError => LaunchErrorMessage is not null;

    [RelayCommand]
    private async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var record = await _repository.LoadAsync(_slug, cancellationToken).ConfigureAwait(true);
            ApplyRecord(record);
            OptionsTab = new InstanceOptionsTabViewModel(
                _slug, record.Metadata.Name, record.Metadata.Launch, record.Metadata.Backups,
                _instanceService, _backupService, _appEnvironment, _overlay, _clock, _toasts);
            await OptionsTab.Backups.RefreshAsync(cancellationToken).ConfigureAwait(true);

            var worlds = await _repository.ListWorldsAsync(_slug, cancellationToken).ConfigureAwait(true);
            Worlds.Clear();
            foreach (var world in worlds.OrderByDescending(file => file.LastModifiedUtc))
            {
                Worlds.Add(new InstanceWorldRowViewModel(world, _clock.UtcNow));
            }

            HasWorlds = Worlds.Count > 0;

            await ModsTab.RefreshAsync(cancellationToken).ConfigureAwait(true);
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
        var backupProgress = new Progress<InstanceBackupProgress>(report
            => AutoBackupProgressText = UiText.Instance.Backups.AutoBackupProgress(report.FilesProcessed, report.TotalFiles));
        try
        {
            var outcome = await _launcher.LaunchAsync(_slug, backupProgress, CancellationToken.None).ConfigureAwait(true);
            if (outcome.AutoBackupFailed)
            {
                _toasts.Show(ToastTone.Warning, UiText.Toasts.AutoBackupFailedTitle, UiText.Toasts.AutoBackupFailedMessage);
            }

            // Le lancement vient de tronquer le journal : les pastilles du lancement précédent ne
            // décrivent plus rien, elles disparaissent maintenant plutôt qu'à la sortie du jeu.
            await ModsTab.ResetLogInsightsAsync(CancellationToken.None).ConfigureAwait(true);
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
            AutoBackupProgressText = string.Empty;
            IsLaunching = false;
        }
    }

    private bool CanPlay() => !IsRunning && !IsLaunching && !IsLoading;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void RequestStop()
        => _overlay.Show(new StopInstanceDialogViewModel(Name, () => _tracker.RequestStop(_slug), _overlay));

    /// <summary>
    /// « Vérifier l'instance » (menu du header) : diagnostic local hors ligne, voir
    /// <see cref="InstanceDoctor"/>. Le dernier résultat de vérification de mises à jour connu pour
    /// cette session (s'il y en a un) alimente la vérification 4 sans jamais interroger le ModDB
    /// ici — la lecture du cache est locale, seule la vérification qui l'a rempli l'était moins.
    /// </summary>
    [RelayCommand]
    private async Task CheckInstanceAsync(CancellationToken cancellationToken = default)
    {
        var lastUpdateCheck = _updateCache.TryGet(_slug);
        var report = await _doctor.DiagnoseAsync(_slug, lastUpdateCheck, cancellationToken).ConfigureAwait(true);

        _overlay.Show(new InstanceDoctorDialogViewModel(
            report,
            navigateToVersions: () =>
            {
                _overlay.Close();
                GoToVersions();
            },
            openModsTab: () =>
            {
                _overlay.Close();
                SelectTab(InstanceDetailTab.Mods);
            },
            // Le diagnostic se referme AVANT que le réseau n'entre en jeu : InstanceDoctor reste
            // hors ligne par construction, et c'est ce clic-ci qui ouvre la machinerie
            // d'installation, exactement celle du navigateur de mods.
            installMod: modIdString =>
            {
                _overlay.Close();
                SelectTab(InstanceDetailTab.Mods);

                // CancellationToken.None explicite, et non celui du diagnostic : ce clic arrive
                // APRÈS que la commande de vérification a rendu la main, donc son jeton ne veut
                // plus rien dire ici. L'installation a sa propre durée de vie.
                return ModsTab.InstallByIdentifierAsync(modIdString, CancellationToken.None);
            },
            // Même construction que l'installation d'une dépendance : le diagnostic se referme
            // AVANT que le réseau n'entre en jeu, et c'est ce clic-ci qui l'appelle. Le docteur
            // reste hors ligne par construction.
            checkModUpdates: () =>
            {
                _overlay.Close();
                SelectTab(InstanceDetailTab.Mods);

                return ModsTab.CheckUpdatesCommand.ExecuteAsync(null);
            },
            _overlay));
    }

    [RelayCommand]
    private void Rename() => _overlay.Show(new RenameDialogViewModel(_slug, Name, _instanceService, _overlay, ReloadHeaderAsync));

    [RelayCommand]
    private void Duplicate() => _overlay.Show(new DuplicateDialogViewModel(_slug, Name, _instanceService, _overlay, OnDuplicatedAsync));

    [RelayCommand]
    private void Delete() => _overlay.Show(new DeleteInstanceDialogViewModel(_slug, Name, _instanceService, _overlay, _dispatcher, OnDeletedAsync));

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
        ModsTab.SetInstanceName(record.Metadata.Name);

        // L'onglet Options aussi : ses dialogues de sauvegarde NOMMENT l'instance, et il n'est
        // reconstruit qu'au chargement de la page, pas à chaque renommage.
        OptionsTab.SetInstanceName(record.Metadata.Name);
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

                // Le journal du lancement est complet à cet instant précis, et c'est le seul
                // moment où il a quelque chose de nouveau à dire : les pastilles de l'onglet Mods
                // se recalculent ici sans que l'utilisateur ait à rouvrir quoi que ce soit.
                _ = ModsTab.ReloadAfterExitAsync();
            }
        });
    }

    partial void OnLaunchErrorMessageChanged(string? value) => OnPropertyChanged(nameof(ShowLaunchError));

    /// <summary>
    /// Se désabonne de <see cref="RunningInstanceTracker"/> (voir <see cref="Home.InstanceCardViewModel.Dispose"/>
    /// pour la même raison) et dispose <see cref="ModsTab"/>, qui porte le jeton d'annulation d'un
    /// éventuel « Tout mettre à jour » en cours.
    /// </summary>
    public void Dispose()
    {
        _tracker.StatusChanged -= OnTrackerStatusChanged;
        ModsTab.Dispose();
    }
}