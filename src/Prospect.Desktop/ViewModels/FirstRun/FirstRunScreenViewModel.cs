using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Auth;
using Prospect.Core.GameVersions;
using Prospect.Core.Migration;
using Prospect.Core.Settings;
using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Migration;

namespace Prospect.Desktop.ViewModels.FirstRun;

/// <summary>
/// Écran de premier lancement (design/ui_kits/launcher/screen-firstrun.jsx) : checklist affichée
/// UNE SEULE FOIS, la toute première fois que l'application démarre (voir <see cref="HasBeenSeen"/>
/// et <c>Prospect.Desktop.ViewModels.Shell.ShellViewModel.ShowFirstRunIfNeeded</c>), avec un retour
/// possible depuis les Réglages (« Revoir le premier lancement »). Affiché sur le même panneau
/// modal que le wizard (<see cref="IOverlayService"/>) plutôt que comme une page à part : le voile
/// plein de ShellView.axaml en tient lieu, le fond flou du handoff v2 étant volontairement ignoré
/// (tokens actuels uniquement, voir docs/architecture.md).
/// </summary>
/// <remarks>
/// La maquette statique montrait trois cartes figées (dossier de données, compte Vintage Story,
/// version du jeu) partageant un bouton « Configurer » générique. Cette checklist-ci est
/// entièrement dynamique : chaque ligne lit l'état réel d'un service. La ligne « compte » avait
/// été délibérément omise à sa création, faute de fonctionnalité derrière — l'afficher aurait été
/// de la simulation. Elle est là depuis que le chantier compte existe : elle montre le vrai état de
/// <see cref="VsAccountService"/> et mène aux Réglages, ce qui referme l'écart documenté à
/// l'époque. Reste conditionnelle, en revanche, l'entrée d'adoption VS Launcher, qui n'apparaît que
/// si <see cref="VslDetector"/> trouve quelque chose.
/// </remarks>
public sealed partial class FirstRunScreenViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IInstalledGameVersionRepository _installedGameVersions;
    private readonly AppPaths _appPaths;
    private readonly VslDetector _detector;
    private readonly Func<VslDetectionResult, AdoptVslViewModel> _adoptFactory;
    private readonly IOverlayService _overlay;
    private readonly VsAccountService _accounts;

    private VslDetectionResult? _detection;

    public FirstRunScreenViewModel(
        SettingsService settings,
        IInstalledGameVersionRepository installedGameVersions,
        AppPaths appPaths,
        VslDetector detector,
        Func<VslDetectionResult, AdoptVslViewModel> adoptFactory,
        IOverlayService overlay,
        VsAccountService accounts)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(installedGameVersions);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(adoptFactory);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(accounts);

        _settings = settings;
        _installedGameVersions = installedGameVersions;
        _appPaths = appPaths;
        _detector = detector;
        _adoptFactory = adoptFactory;
        _overlay = overlay;
        _accounts = accounts;
    }

    /// <summary>Levée quand la ligne « Version du jeu » demande la navigation vers la page Versions.</summary>
    public event EventHandler? NavigateToVersionsRequested;

    /// <summary>Levée quand la ligne « Compte Vintage Story » demande d'aller se connecter (Réglages, Comptes).</summary>
    public event EventHandler? NavigateToAccountSettingsRequested;

    /// <summary>Levée quand une adoption VS Launcher ouverte depuis cet écran se termine : l'Accueil doit se rafraîchir.</summary>
    public event EventHandler<VslAdoptionOutcome>? VslAdopted;

    /// <summary>Vrai une fois l'écran déjà vu (voir <see cref="ProspectSettings.HasSeenFirstRun"/>).</summary>
    public bool HasBeenSeen => _settings.Current.HasSeenFirstRun;

    /// <summary>Racine réelle des données Prospect, affichée en mono sur la première ligne de la checklist.</summary>
    public string DataFolderPath => _appPaths.RootDirectory;

    /// <summary>La checklist elle-même, reconstruite à chaque <see cref="InitializeCommand"/>.</summary>
    public ObservableCollection<FirstRunStepViewModel> Steps { get; } = [];

    /// <summary>
    /// Relance l'observation des vrais services (aucune simulation) : appelée au premier affichage
    /// automatique comme à chaque « Revoir le premier lancement » depuis les Réglages, l'état a pu
    /// changer entre-temps (une version installée depuis, VS Launcher installé depuis...).
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _detection = await _detector.DetectAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
        var scan = await _installedGameVersions.ScanAsync(cancellationToken).ConfigureAwait(true);

        BuildSteps(scan.Installed, _detection);
    }

    private void BuildSteps(IReadOnlyList<InstalledGameVersion> installed, VslDetectionResult detection)
    {
        Steps.Clear();

        // Toujours satisfaite : il n'existe pas d'état « non configuré » en MVP (relocaliser le
        // dossier racine est hors périmètre, voir ProspectSettings), la ligne est purement
        // informative sur où vivent les données.
        Steps.Add(new FirstRunStepViewModel(
            IconKey: "folder",
            Title: UiText.FirstRun.DataFolderTitle,
            Subtitle: DataFolderPath,
            IsDone: true));

        var hasInstalledVersion = installed.Count > 0;
        Steps.Add(new FirstRunStepViewModel(
            IconKey: "hard-drive",
            Title: UiText.FirstRun.GameVersionTitle,
            Subtitle: hasInstalledVersion
                ? UiText.FirstRun.InstalledVersionsSummary(installed.Count, installed[0].Version.ToString())
                : UiText.FirstRun.NoVersionInstalled,
            IsDone: hasInstalledVersion,
            ActionLabel: hasInstalledVersion ? null : UiText.FirstRun.InstallVersionAction,
            ActionCommand: hasInstalledVersion ? null : GoToVersionsCommand));

        // Facultative au sens du produit : le jeu se télécharge et se joue en solo sans compte
        // (docs/research/vslauncher-et-distribution.md, verdict). La ligne dit donc à quoi ça sert
        // plutôt que de faire peser une obligation, et elle se coche dès qu'une session existe.
        var session = _accounts.CurrentSession;
        Steps.Add(new FirstRunStepViewModel(
            IconKey: "user",
            Title: UiText.FirstRun.AccountTitle,
            Subtitle: session is null
                ? UiText.FirstRun.AccountSignedOut
                : UiText.Account.SignedInSubtitle(session.PlayerName),
            IsDone: session is not null,
            ActionLabel: session is null ? UiText.FirstRun.AccountSignInAction : null,
            ActionCommand: session is null ? GoToAccountSettingsCommand : null));

        // Conditionnelle : absente tant que VslDetector ne trouve rien (docs de la mission,
        // « l'entrée d'adoption VS Launcher quand la détection de la #31 trouve quelque chose »).
        if (detection.IsDetected && detection.HasAnyContent)
        {
            Steps.Add(new FirstRunStepViewModel(
                IconKey: "download",
                Title: UiText.FirstRun.VslDetectedTitle,
                Subtitle: UiText.Migration.DetectionSummary(detection.InstallationCount, detection.GameVersionCount),
                IsDone: false,
                ActionLabel: UiText.FirstRun.AdoptAction,
                ActionCommand: OpenVslAdoptionCommand));
        }
    }

    // Quitte l'écran pour aller installer une version (page Versions) : fermer d'abord le panneau
    // modal plutôt que de le laisser par-dessus la page cible, exactement ce que ferait un
    // utilisateur qui a fini de lire cet écran.
    [RelayCommand]
    private async Task GoToVersionsAsync()
    {
        await MarkSeenAsync().ConfigureAwait(true);
        _overlay.Close();
        NavigateToVersionsRequested?.Invoke(this, EventArgs.Empty);
    }

    // Même sortie que GoToVersions : fermer le panneau avant d'emmener l'utilisateur ailleurs,
    // ici vers Réglages > Comptes, le seul endroit où la connexion se fait.
    [RelayCommand]
    private async Task GoToAccountSettingsAsync()
    {
        await MarkSeenAsync().ConfigureAwait(true);
        _overlay.Close();
        NavigateToAccountSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    // Remplace ce panneau par le dialogue d'adoption (même IOverlayService à slot unique que le
    // reste de l'app) : Show() se charge lui-même de disposer l'actif précédent s'il l'était.
    [RelayCommand]
    private async Task OpenVslAdoptionAsync()
    {
        if (_detection is not { } detection)
        {
            return;
        }

        await MarkSeenAsync().ConfigureAwait(true);

        var adopt = _adoptFactory(detection);
        adopt.Completed += OnAdoptionCompleted;
        _overlay.Show(adopt);
    }

    private void OnAdoptionCompleted(object? sender, VslAdoptionOutcome outcome)
    {
        if (sender is AdoptVslViewModel adopt)
        {
            adopt.Completed -= OnAdoptionCompleted;
        }

        VslAdopted?.Invoke(this, outcome);
    }

    // Commencer et Passer sont deux boutons distincts côté maquette (poids visuel différent), mais
    // au même effet : ni l'un ni l'autre ne bloque sur l'état de la checklist, voir la docstring de
    // classe. Deux commandes séparées plutôt qu'une seule partagée pour que le binding XAML et les
    // futurs tests puissent les distinguer si leur comportement diverge un jour.
    [RelayCommand]
    private Task StartAsync() => DismissAsync();

    [RelayCommand]
    private Task SkipAsync() => DismissAsync();

    private async Task DismissAsync()
    {
        await MarkSeenAsync().ConfigureAwait(true);
        _overlay.Close();
    }

    private Task MarkSeenAsync() => _settings.UpdateAsync(current => current with { HasSeenFirstRun = true });
}