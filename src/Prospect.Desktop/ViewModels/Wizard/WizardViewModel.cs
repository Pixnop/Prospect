using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Http;
using Prospect.Core.Instances;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Wizard;

/// <summary>
/// Wizard de création d'instance en 4 étapes (design/ui_kits/launcher/screen-wizard.jsx) : nom
/// (avec aperçu du slug), version du jeu, icône, récapitulatif + création.
/// </summary>
/// <remarks>
/// L'étape 2 est le vrai sélecteur du catalogue : versions installées d'abord, puis versions
/// téléchargeables, triées de la plus récente à la plus ancienne et filtrables par canal. Choisir
/// une version absente du disque déclenche son installation, avec progression, avant la création
/// de l'instance. C'est volontairement séquentiel plutôt que parallèle : une instance qui
/// existerait avant sa version serait immédiatement injouable, et le wizard n'aurait plus d'endroit
/// où montrer l'échec si le téléchargement se passe mal.
/// </remarks>
public sealed partial class WizardViewModel : ObservableObject, IProgress<GameInstallProgress>
{
    /// <summary>
    /// Icônes proposées à l'étape 3 : aucune icône d'instance n'est fournie par le handoff design
    /// (voir design/readme.md, ICONOGRAPHY), donc un sous-ensemble curé du jeu de glyphes existant
    /// en attendant la personnalisation par fichier, prévue plus tard.
    /// </summary>
    private static readonly (string Key, string IconKey, string Label)[] IconCatalog =
    [
        ("default", "layers", "Par défaut"),
        ("package", "package", "Caisse"),
        ("star", "star", "Étoile"),
        ("hard-drive", "hard-drive", "Disque"),
        ("image", "image", "Image"),
    ];

    private readonly InstanceService _instanceService;
    private readonly IOverlayService _overlay;
    private readonly IGameVersionCatalog _catalog;
    private readonly IInstalledGameVersionRepository _versions;
    private readonly GameInstallService _installService;
    private readonly IUiDispatcher _dispatcher;

    private readonly List<GameVersionChoiceViewModel> _allChoices = [];

    private CancellationTokenSource? _createCancellation;

    public WizardViewModel(
        InstanceService instanceService,
        IOverlayService overlay,
        IGameVersionCatalog catalog,
        IInstalledGameVersionRepository versions,
        GameInstallService installService,
        IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(installService);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _instanceService = instanceService;
        _overlay = overlay;
        _catalog = catalog;
        _versions = versions;
        _installService = installService;
        _dispatcher = dispatcher;
        _selectedIconKey = IconCatalog[0].Key;

        RecomputeSteps();
        RecomputeIconChoices();
    }

    /// <summary>Levé une fois l'instance effectivement créée (icône appliquée comprise). L'abonné referme le wizard.</summary>
    public event EventHandler<InstanceRecord>? Created;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(IsNameStep))]
    [NotifyPropertyChangedFor(nameof(IsVersionStep))]
    [NotifyPropertyChangedFor(nameof(IsIconStep))]
    [NotifyPropertyChangedFor(nameof(IsSummaryStep))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private int _currentStepIndex;

    partial void OnCurrentStepIndexChanged(int value) => RecomputeSteps();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SlugPreview))]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyPropertyChangedFor(nameof(IsNameStepValid))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _name = string.Empty;

    partial void OnNameChanged(string value) => NameTouched = true;

    /// <summary>
    /// Modèle « touched » : vrai dès la première frappe dans le champ nom (voir
    /// <see cref="OnNameChanged"/>) ou dès une tentative d'avancer alors qu'il est encore vide
    /// (voir <see cref="Next"/>). Tant qu'il est faux, <see cref="NameError"/> reste
    /// <see langword="null"/> même si le nom est vide : un champ tout juste ouvert n'a encore rien
    /// à reprocher à l'utilisateur, seule une vraie interaction déclenche le message. Ne redevient
    /// jamais faux une fois vrai (docs/architecture.md n'a pas besoin de plus qu'un booléen simple
    /// ici, il n'y a qu'un seul champ à suivre).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    private bool _nameTouched;

    [ObservableProperty]
    private string _selectedIconKey;

    partial void OnSelectedIconKeyChanged(string value) => RecomputeIconChoices();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelInstallCommand))]
    private bool _isCreating;

    [ObservableProperty]
    private bool _isInstallingVersion;

    [ObservableProperty]
    private string _installPhaseText = string.Empty;

    [ObservableProperty]
    private string _installDetailText = string.Empty;

    [ObservableProperty]
    private double _installProgressPercent;

    [ObservableProperty]
    private bool _isInstallIndeterminate;

    [ObservableProperty]
    private string? _createError;

    [ObservableProperty]
    private bool _isLoadingVersions;

    /// <summary>Message affiché quand le catalogue distant n'a pas pu être consulté.</summary>
    [ObservableProperty]
    private string? _versionsWarning;

    /// <summary>0 = tous les canaux, 1 = stable, 2 = unstable, 3 = pre.</summary>
    [ObservableProperty]
    private int _channelFilterIndex;

    partial void OnChannelFilterIndexChanged(int value) => ApplyChannelFilter();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVersionStepValid))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(SelectedVersionText))]
    [NotifyPropertyChangedFor(nameof(SummaryNote))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private GameVersionChoiceViewModel? _selectedVersion;

    /// <summary>Versions proposées après filtrage par canal : installées d'abord, puis à télécharger.</summary>
    public ObservableCollection<GameVersionChoiceViewModel> VersionChoices { get; } = [];

    public IReadOnlyList<string> StepLabels { get; } = UiText.Wizard.StepLabels;

    [ObservableProperty]
    private IReadOnlyList<WizardStepIndicatorViewModel> _steps = [];

    [ObservableProperty]
    private IReadOnlyList<IconChoiceOption> _iconChoices = [];

    public bool IsNameStep => CurrentStepIndex == 0;

    public bool IsVersionStep => CurrentStepIndex == 1;

    public bool IsIconStep => CurrentStepIndex == 2;

    public bool IsSummaryStep => CurrentStepIndex == 3;

    /// <summary>Aperçu du slug de dossier, recalculé à chaque frappe (voir <see cref="InstanceSlugGenerator.Slugify"/>).</summary>
    public string SlugPreview => InstanceSlugGenerator.Slugify(Name);

    /// <summary>
    /// Message d'erreur affiché sous le champ, ou <see langword="null"/> tant que rien ne le
    /// justifie : soit le nom est valide, soit le champ n'a pas encore été touché
    /// (<see cref="NameTouched"/>). La validité elle-même (<see cref="IsNameStepValid"/>) ne
    /// dépend jamais du touché : Suivant reste désactivé sur un nom vide dès le premier instant,
    /// seul le MESSAGE attend une interaction avant de s'afficher.
    /// </summary>
    public string? NameError => NameTouched && string.IsNullOrWhiteSpace(Name) ? UiText.Wizard.NameRequired : null;

    public bool IsNameStepValid => !string.IsNullOrWhiteSpace(Name);

    public bool IsVersionStepValid => SelectedVersion is not null;

    public string SelectedVersionText => SelectedVersion?.VersionText ?? string.Empty;

    /// <summary>Phrase de récapitulatif : rien à télécharger, ou téléchargement à prévoir.</summary>
    public string SummaryNote => SelectedVersion switch
    {
        null => UiText.Wizard.SummaryNoVersion,
        { IsInstalled: true } choice => UiText.Wizard.SummaryAlreadyInstalled(choice.VersionText),
        var choice => UiText.Wizard.SummaryWillDownload(choice.VersionText),
    };

    private bool IsCurrentStepValid => CurrentStepIndex switch
    {
        0 => IsNameStepValid,
        1 => IsVersionStepValid,
        _ => true,
    };

    public bool CanGoNext => !IsLastStep && IsCurrentStepValid;

    public bool CanGoBack => CurrentStepIndex > 0;

    public bool IsLastStep => CurrentStepIndex == StepLabels.Count - 1;

    /// <inheritdoc />
    public void Report(GameInstallProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _dispatcher.Post(() =>
        {
            InstallPhaseText = UiText.Versions.PhaseLabel(value.Phase);
            IsInstallIndeterminate = value.Ratio is null;
            InstallProgressPercent = (value.Ratio ?? 0d) * 100d;
            InstallDetailText = value.Phase == GameInstallPhase.Downloading
                ? UiText.Versions.DownloadDetail(
                    ByteSizeFormatter.FormatProgress(value.ReceivedBytes, value.TotalBytes),
                    ByteSizeFormatter.FormatSpeed(value.BytesPerSecond))
                : string.Empty;
        });
    }

    /// <summary>
    /// Charge les versions proposées à l'étape 2 : le disque d'abord (toujours disponible), puis
    /// le catalogue distant si on peut le joindre.
    /// </summary>
    [RelayCommand]
    public async Task LoadVersionsAsync(CancellationToken cancellationToken = default)
    {
        IsLoadingVersions = true;
        try
        {
            var scan = await _versions.ScanAsync(cancellationToken).ConfigureAwait(true);
            var installed = scan.Installed.Select(entry => entry.Version).ToHashSet();

            _allChoices.Clear();
            _allChoices.AddRange(scan.Installed.Select(entry => new GameVersionChoiceViewModel(
                entry.Version,
                isInstalled: true,
                UiText.Wizard.VersionInstalled,
                Select)));

            _allChoices.AddRange(await LoadCatalogChoicesAsync(installed, cancellationToken).ConfigureAwait(true));
        }
        finally
        {
            IsLoadingVersions = false;
            ApplyChannelFilter();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        // CanGoNext bloque déjà le bouton tant que le nom est vide ; ce marquage reste une
        // défense en profondeur (un appelant qui invoquerait la commande directement en
        // contournant CanExecute doit quand même voir l'erreur s'afficher s'il revient en arrière).
        if (IsNameStep)
        {
            NameTouched = true;
        }

        CurrentStepIndex++;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentStepIndex--;

    [RelayCommand]
    private void Cancel()
    {
        _createCancellation?.Cancel();
        _overlay.Close();
    }

    [RelayCommand(CanExecute = nameof(IsCreating))]
    private void CancelInstall() => _createCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        if (SelectedVersion is not { } choice)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _createCancellation = cancellation;
        IsCreating = true;
        CreateError = null;

        try
        {
            if (!choice.IsInstalled)
            {
                IsInstallingVersion = true;
                await _installService.InstallAsync(choice.Version, this, cancellation.Token).ConfigureAwait(true);
                IsInstallingVersion = false;
            }

            await CreateInstanceAsync(choice.Version, cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            CreateError = UiText.Wizard.InstallCanceled;
        }
        catch (InstanceNameInvalidException exception)
        {
            CreateError = exception.Message;
        }
        catch (Exception exception) when (exception is GameVersionNotAvailableException or GameInstallFailedException or DownloadFailedException)
        {
            CreateError = exception.Message;
        }
        finally
        {
            IsCreating = false;
            IsInstallingVersion = false;
            _createCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task CreateInstanceAsync(GameVersion version, CancellationToken cancellationToken)
    {
        var created = await _instanceService.CreateAsync(Name.Trim(), version, cancellationToken).ConfigureAwait(true);

        if (SelectedIconKey != IconCatalog[0].Key)
        {
            created = await _instanceService.SetIconAsync(created.Slug, $"builtin:{SelectedIconKey}", cancellationToken).ConfigureAwait(true);
        }

        Created?.Invoke(this, created);
        _overlay.Close();
    }

    private async Task<IEnumerable<GameVersionChoiceViewModel>> LoadCatalogChoicesAsync(
        HashSet<GameVersion> installed,
        CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await _catalog.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
            VersionsWarning = catalog.Freshness == GameCatalogFreshness.Stale ? UiText.Versions.StaleCatalog : null;

            return catalog.Versions
                .Where(entry => !installed.Contains(entry.Version))
                .Select(entry => (Entry: entry, Asset: _installService.FindInstallableAsset(entry)))
                .Where(pair => pair.Asset is not null)
                .Select(pair => new GameVersionChoiceViewModel(
                    pair.Entry.Version,
                    isInstalled: false,
                    UiText.Wizard.VersionToDownload(pair.Asset!.DisplaySize),
                    Select))
                .ToArray();
        }
        catch (GameCatalogUnavailableException)
        {
            // Hors ligne : le wizard reste utilisable avec ce qui est déjà installé.
            VersionsWarning = UiText.Versions.UnavailableCatalog;

            return [];
        }
    }

    // Les installées d'abord (recommandées, rien à télécharger), puis les téléchargeables, chaque
    // groupe de la plus récente à la plus ancienne.
    private void ApplyChannelFilter()
    {
        var previous = SelectedVersion?.Version;

        VersionChoices.Clear();
        foreach (var choice in _allChoices
                     .Where(MatchesChannelFilter)
                     .OrderByDescending(choice => choice.IsInstalled)
                     .ThenByDescending(choice => choice.Version))
        {
            VersionChoices.Add(choice);
        }

        var restored = previous is null ? null : VersionChoices.FirstOrDefault(choice => choice.Version == previous);
        Select(restored ?? VersionChoices.FirstOrDefault());
    }

    private bool MatchesChannelFilter(GameVersionChoiceViewModel choice) => ChannelFilterIndex switch
    {
        1 => choice.Version.Channel == GameVersionChannel.Stable,
        2 => choice.Version.Channel is GameVersionChannel.Rc or GameVersionChannel.Dev,
        3 => choice.Version.Channel == GameVersionChannel.Pre,
        _ => true,
    };

    private void Select(GameVersionChoiceViewModel? choice)
    {
        SelectedVersion = choice;
        foreach (var candidate in _allChoices)
        {
            candidate.IsSelected = ReferenceEquals(candidate, choice);
        }
    }

    private bool CanCreate() => IsLastStep && IsNameStepValid && IsVersionStepValid && !IsCreating;

    private void RecomputeSteps()
    {
        var steps = new WizardStepIndicatorViewModel[StepLabels.Count];
        for (var index = 0; index < StepLabels.Count; index++)
        {
            steps[index] = new WizardStepIndicatorViewModel(index + 1, StepLabels[index], isDone: index < CurrentStepIndex, isCurrent: index == CurrentStepIndex);
        }

        Steps = steps;
    }

    private void RecomputeIconChoices()
        => IconChoices = IconCatalog
            .Select(icon => new IconChoiceOption(icon.Key, icon.IconKey, icon.Label, icon.Key == SelectedIconKey, SelectIcon))
            .ToArray();

    private void SelectIcon(string key) => SelectedIconKey = key;
}