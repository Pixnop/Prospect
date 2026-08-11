using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Launching;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Home;

/// <summary>
/// Une carte de la grille d'Accueil (design/components/launcher/InstanceCard.jsx). Le bouton
/// « Jouer » bascule Jouer → (lancement) → En cours avec « Arrêter » (confirmation avant kill).
/// Une carte n'a pas de place pour un bandeau d'erreur : les échecs de pré-lancement s'affichent
/// en toast (voir <see cref="LaunchErrorPresenter"/>, partagé avec la page de détail qui, elle,
/// a la place pour un bandeau). Les actions du menu ouvrent chacune un dialogue via
/// <see cref="IOverlayService"/> et redemandent un rafraîchissement de l'Accueil après un succès.
/// </summary>
public sealed partial class InstanceCardViewModel : ObservableObject, IDisposable
{
    private readonly InstanceService _instanceService;
    private readonly GameLauncher _launcher;
    private readonly RunningInstanceTracker _tracker;
    private readonly IModUpdateCheckCache _updateCache;
    private readonly IOverlayService _overlay;
    private readonly IToastService _toasts;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<Task> _requestRefresh;

    public InstanceCardViewModel(
        InstanceRecord record,
        string lastPlayedText,
        InstanceService instanceService,
        GameLauncher launcher,
        RunningInstanceTracker tracker,
        IModUpdateCheckCache updateCache,
        IOverlayService overlay,
        IToastService toasts,
        IUiDispatcher dispatcher,
        Func<Task> requestRefresh)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(updateCache);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(requestRefresh);

        _instanceService = instanceService;
        _launcher = launcher;
        _tracker = tracker;
        _updateCache = updateCache;
        _overlay = overlay;
        _toasts = toasts;
        _dispatcher = dispatcher;
        _requestRefresh = requestRefresh;

        Slug = record.Slug;
        Name = record.Metadata.Name;
        Version = record.Metadata.GameVersion.ToString();
        ChannelBadgeTone = ChannelBadgePresentation.ToBadgeTone(record.Metadata.GameVersion.Channel);
        LastLaunchedUtc = record.Metadata.LastLaunchedUtc;
        LastPlayedText = lastPlayedText;
        IconKey = InstanceIconKeyResolver.Resolve(record.Metadata.Icon);
        _isRunning = tracker.IsRunning(Slug);
        _updateCount = updateCache.TryGet(Slug)?.UpdateCount ?? 0;

        _tracker.StatusChanged += OnTrackerStatusChanged;
        _updateCache.Changed += OnUpdateCacheChanged;
    }

    /// <summary>Levé quand la carte (hors bouton Jouer/Arrêter et menu) est cliquée : ouvre la page de détail.</summary>
    public event EventHandler<string>? OpenRequested;

    public string Slug { get; }

    public string Name { get; }

    /// <summary>Valeur machine (voir design/readme.md, « Content fundamentals ») : liée en FontMono côté vue.</summary>
    public string Version { get; }

    /// <summary>Une des quatre teintes de badge du design (voir <see cref="ChannelBadgePresentation"/>).</summary>
    public string ChannelBadgeTone { get; }

    /// <summary>Instant brut, utilisé pour le tri ; <see cref="LastPlayedText"/> porte le texte déjà humanisé.</summary>
    public DateTimeOffset? LastLaunchedUtc { get; }

    public string LastPlayedText { get; }

    /// <summary>Clé résolue par <see cref="Prospect.Desktop.Converters.IconKeyToGeometryConverter"/>.</summary>
    public string IconKey { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestStopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool _isLaunching;

    /// <summary>
    /// Nombre de mises à jour trouvées par la dernière vérification connue, tous canaux confondus.
    /// Zéro tant qu'aucune vérification n'a eu lieu cette session (voir <see cref="IModUpdateCheckCache"/>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdates))]
    [NotifyPropertyChangedFor(nameof(UpdatesBadgeText))]
    private int _updateCount;

    /// <summary>Vrai quand la pastille discrète « N mises à jour » de la maquette doit s'afficher.</summary>
    public bool HasUpdates => UpdateCount > 0;

    /// <summary>Texte de la pastille, par exemple « 3 mises à jour ».</summary>
    public string UpdatesBadgeText => UiText.Home.UpdatesBadge(UpdateCount);

    [RelayCommand]
    private void Open() => OpenRequested?.Invoke(this, Slug);

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        IsLaunching = true;
        try
        {
            await _launcher.LaunchAsync(Slug, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InstanceNotFoundException or InstanceAlreadyRunningException
                                        or GameVersionNotInstalledException or Core.Runtime.RuntimeNotAvailableException
                                        or MacLaunchNotSupportedException)
        {
            var presentation = LaunchErrorPresenter.Describe(ex);
            _toasts.Show(ToastTone.Error, presentation.Title, presentation.Message);
        }
        finally
        {
            IsLaunching = false;
        }
    }

    private bool CanPlay() => !IsRunning && !IsLaunching;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void RequestStop()
        => _overlay.Show(new StopInstanceDialogViewModel(Name, () => _tracker.RequestStop(Slug), _overlay));

    [RelayCommand]
    private void Rename() => _overlay.Show(new RenameDialogViewModel(Slug, Name, _instanceService, _overlay, () => _requestRefresh()));

    [RelayCommand]
    private void Duplicate() => _overlay.Show(new DuplicateDialogViewModel(Slug, Name, _instanceService, _overlay, () => _requestRefresh()));

    [RelayCommand]
    private void Delete() => _overlay.Show(new DeleteInstanceDialogViewModel(Slug, Name, _instanceService, _overlay, () => _requestRefresh()));

    private void OnTrackerStatusChanged(object? sender, RunningInstanceStatus status)
    {
        if (!string.Equals(status.Slug, Slug, StringComparison.Ordinal))
        {
            return;
        }

        _dispatcher.Post(() => IsRunning = status.State == RunningInstanceState.Started);
    }

    // Garde la pastille vivante même quand la carte n'a pas été reconstruite depuis la dernière
    // vérification : ShowHome() ne rafraîchit pas l'Accueil (voir ShellViewModel), donc sans cet
    // abonnement une vérification lancée depuis l'onglet Mods n'apparaîtrait qu'au rafraîchissement
    // suivant plutôt qu'au retour immédiat sur la carte.
    private void OnUpdateCacheChanged(object? sender, ModUpdateCacheChange change)
    {
        if (!string.Equals(change.Slug, Slug, StringComparison.Ordinal))
        {
            return;
        }

        _dispatcher.Post(() => UpdateCount = change.Report?.UpdateCount ?? 0);
    }

    /// <summary>
    /// Se désabonne de <see cref="RunningInstanceTracker"/> et <see cref="IModUpdateCheckCache"/> :
    /// sans ça, chaque rafraîchissement de l'Accueil (<c>HomeViewModel.RefreshAsync</c>, qui
    /// reconstruit toutes les cartes) laisserait ces singletons — qui vivent toute la durée de
    /// l'application — retenir indéfiniment des cartes remplacées, plus leurs abonnements.
    /// <see cref="HomeViewModel"/> appelle cette méthode avant d'abandonner une carte.
    /// </summary>
    public void Dispose()
    {
        _tracker.StatusChanged -= OnTrackerStatusChanged;
        _updateCache.Changed -= OnUpdateCacheChanged;
    }
}