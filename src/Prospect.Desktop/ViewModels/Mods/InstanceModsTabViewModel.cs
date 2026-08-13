using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.Http;
using Prospect.Core.ModDb;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Mods;

/// <summary>
/// Onglet Mods de la page de détail d'une instance (design/ui_kits/launcher/screen-instance.jsx et
/// composant <c>ModRow</c>) : liste des mods installés, activation, désinstallation avec
/// vérification inverse, raccourci vers le navigateur préfiltré sur cette instance, et détection /
/// application des mises à jour ModDB (feature 4b du MVP).
/// </summary>
public sealed partial class InstanceModsTabViewModel : ObservableObject, IDisposable
{
    private readonly string _slug;
    private readonly IInstalledModRepository _repository;
    private readonly ModInstallService _installService;
    private readonly ModUpdateChecker _updateChecker;
    private readonly IModUpdateCheckCache _updateCache;
    private readonly IClock _clock;
    private readonly IOverlayService _overlay;
    private readonly IToastService _toasts;
    private readonly IExternalUrlOpener _urlOpener;
    private readonly IModLogoCache _logoCache;

    private InstanceUpdateReport? _lastReport;
    private string _instanceName;

    public InstanceModsTabViewModel(
        string slug,
        IInstalledModRepository repository,
        ModInstallService installService,
        ModUpdateChecker updateChecker,
        IModUpdateCheckCache updateCache,
        IClock clock,
        IOverlayService overlay,
        IToastService toasts,
        IExternalUrlOpener urlOpener,
        IModLogoCache logoCache)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(installService);
        ArgumentNullException.ThrowIfNull(updateChecker);
        ArgumentNullException.ThrowIfNull(updateCache);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(urlOpener);
        ArgumentNullException.ThrowIfNull(logoCache);

        _slug = slug;
        _repository = repository;
        _installService = installService;
        _updateChecker = updateChecker;
        _updateCache = updateCache;
        _clock = clock;
        _overlay = overlay;
        _toasts = toasts;
        _urlOpener = urlOpener;
        _logoCache = logoCache;
        _instanceName = slug;
        ModsDirectoryText = repository.GetModsDirectory(slug);

        // Le dernier résultat connu cette session (voir IModUpdateCheckCache) est repris tel quel à
        // l'ouverture de la page : le MVP ne relance jamais de vérification automatique, donc c'est
        // la seule façon pour l'horodatage et les badges d'avoir un état cohérent avant le premier
        // clic sur « Vérifier les mises à jour » de CETTE ouverture de la page.
        ApplyReport(updateCache.TryGet(slug));
    }

    /// <summary>Demande l'ouverture du navigateur de mods, préfiltré sur cette instance.</summary>
    public event EventHandler<string>? BrowseRequested;

    /// <summary>Mods présents dans <c>data/Mods/</c>, actifs et désactivés.</summary>
    public ObservableCollection<InstalledModRowViewModel> Mods { get; } = [];

    /// <summary>Chemin du dossier, affiché en monospace sous la liste.</summary>
    public string ModsDirectoryText { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasMods;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>« Dernière vérification : … », horodatage humanisé de <see cref="IModUpdateCheckCache"/>.</summary>
    [ObservableProperty]
    private string _lastCheckedText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateAllCommand))]
    private bool _isCheckingUpdates;

    /// <summary>Nombre de mises à jour trouvées par la dernière vérification connue.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableUpdates))]
    [NotifyCanExecuteChangedFor(nameof(UpdateAllCommand))]
    private int _availableUpdateCount;

    /// <summary>Vrai quand le bandeau « N mises à jour disponibles » de la maquette doit s'afficher.</summary>
    public bool HasAvailableUpdates => AvailableUpdateCount > 0;

    public string UpdatesAvailableTitle => UiText.Mods.UpdatesAvailableTitle(AvailableUpdateCount);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateAllCommand))]
    private bool _isUpdatingAll;

    /// <summary>« 2/5 · Carry Capacity », vide tant qu'aucun « Tout mettre à jour » n'est en cours.</summary>
    [ObservableProperty]
    private string _updateAllProgressText = string.Empty;

    private CancellationTokenSource? _updateAllCancellation;

    /// <summary>Relit <c>data/Mods/</c> : le disque est la source de vérité, jamais un état gardé en mémoire.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var mods = await _repository.ScanAsync(_slug, cancellationToken).ConfigureAwait(true);

            Mods.Clear();
            foreach (var mod in mods)
            {
                Mods.Add(new InstalledModRowViewModel(mod, ToggleAsync, RequestUninstallAsync, RequestUpdateAsync, FindUpdateResult(mod)));
            }

            HasMods = Mods.Count > 0;
            SummaryText = UiText.Mods.InstalledSummary(Mods.Count, Mods.Count(row => row.IsEnabled));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Appelle le ModDB en un seul appel (voir <see cref="ModUpdateChecker"/>) et rafraîchit les
    /// badges de chaque ligne, le bandeau d'ensemble et l'horodatage. Aucune vérification
    /// automatique au démarrage en MVP (docs/architecture.md) : c'est toujours un clic explicite.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckUpdates))]
    public async Task CheckUpdatesAsync(CancellationToken cancellationToken = default)
    {
        IsCheckingUpdates = true;
        try
        {
            var report = await _updateChecker.CheckAsync(_slug, cancellationToken: cancellationToken).ConfigureAwait(true);
            _updateCache.Store(_slug, report);
            ApplyReport(report);
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is ModDbApiException or ModDbUnavailableException)
        {
            _toasts.Show(ToastTone.Error, UiText.Mods.CheckUpdatesFailedTitle, exception.Message);
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    private bool CanCheckUpdates() => !IsCheckingUpdates && !IsUpdatingAll;

    /// <summary>
    /// Applique séquentiellement toutes les mises à jour disponibles (bouton « Tout mettre à
    /// jour »). N'installe jamais de nouvelle dépendance (voir <see cref="ModInstallService.ApplyAllUpdatesAsync"/>) :
    /// à traiter ensuite depuis la mise à jour individuelle du mod concerné si besoin.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private async Task UpdateAllAsync()
    {
        if (_lastReport is null)
        {
            return;
        }

        IsUpdatingAll = true;
        _updateAllCancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<BulkUpdateProgress>(report
                => UpdateAllProgressText = report.CompletedCount >= report.TotalCount
                    ? string.Empty
                    : UiText.Mods.BulkUpdateProgress(report.CompletedCount, report.TotalCount, report.CurrentModName));

            var outcome = await _installService
                .ApplyAllUpdatesAsync(_slug, _lastReport.Mods, progress, _updateAllCancellation.Token)
                .ConfigureAwait(true);

            InvalidateUpdateState();
            await RefreshAsync(CancellationToken.None).ConfigureAwait(true);

            _toasts.Show(
                outcome.HasFailures ? ToastTone.Error : ToastTone.Success,
                UiText.Mods.BulkUpdateDoneTitle(outcome.Updated.Count),
                outcome.HasFailures ? UiText.Mods.BulkUpdateFailures(outcome.Failed) : null);
        }
        finally
        {
            UpdateAllProgressText = string.Empty;
            IsUpdatingAll = false;
            _updateAllCancellation?.Dispose();
            _updateAllCancellation = null;
        }
    }

    private bool CanUpdateAll() => !IsCheckingUpdates && !IsUpdatingAll && HasAvailableUpdates;

    /// <summary>Annule un « Tout mettre à jour » en cours, entre deux mods (jamais au milieu d'un remplacement).</summary>
    [RelayCommand(CanExecute = nameof(IsUpdatingAll))]
    private void CancelUpdateAll() => _updateAllCancellation?.Cancel();

    [RelayCommand]
    private void Browse() => BrowseRequested?.Invoke(this, _slug);

    private async Task ToggleAsync(InstalledModRowViewModel row, bool enabled)
    {
        try
        {
            await _installService.SetEnabledAsync(_slug, row.Mod, enabled, CancellationToken.None).ConfigureAwait(true);
            _toasts.Show(
                ToastTone.Info,
                enabled ? UiText.Mods.EnabledTitle : UiText.Mods.DisabledTitle,
                row.Name);
        }
        catch (ModFileNotFoundException)
        {
            _toasts.Show(ToastTone.Error, UiText.Mods.FileGoneTitle, row.Name);
        }

        InvalidateUpdateState();
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Nom affichable de l'instance, pour les dialogues qui la nomment. Posé par la page de détail
    /// une fois l'instance chargée : à la construction de cet onglet, seul le slug est connu.
    /// </summary>
    public void SetInstanceName(string name) => _instanceName = string.IsNullOrWhiteSpace(name) ? _slug : name;

    /// <summary>
    /// Ouvre le dialogue de plan d'installation pour un mod désigné par son <c>modid</c>, dans cette
    /// instance. C'est l'action des lignes de dépendance manquante du docteur.
    /// </summary>
    /// <remarks>
    /// Rien de neuf en dessous : le plan est préparé par le MÊME
    /// <see cref="ModInstallService.PrepareAsync(string, string, ModCompatibilityMode, int?, IProgress{DownloadProgress}?, CancellationToken)"/>
    /// que le navigateur, avec le même dialogue et le même dévoilement « installer quand même ».
    /// Le RÉSEAU n'intervient qu'ici, au clic, après que le diagnostic est refermé : le docteur
    /// lui-même reste hors ligne par construction et n'a gagné aucune dépendance.
    /// </remarks>
    public async Task InstallByIdentifierAsync(string modIdString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(modIdString);

        try
        {
            var plan = await _installService
                .PrepareAsync(_slug, modIdString, cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            _overlay.Show(new ModInstallPlanDialogViewModel(
                plan,
                _instanceName,
                (chosen, selection) => ApplyInstallAsync(chosen, selection),
                releaseId => _installService.PrepareAsync(_slug, modIdString, releaseId: releaseId, cancellationToken: CancellationToken.None),
                _overlay,
                _urlOpener,
                _logoCache));
        }
        catch (ModReleaseNotFoundException exception)
        {
            _toasts.Show(ToastTone.Error, UiText.Mods.NoCompatibleReleaseTitle, exception.Message);
        }
        catch (Exception exception) when (exception is ModDbApiException or ModDbUnavailableException or DownloadFailedException or ModInstallFailedException)
        {
            // Échec réseau au clic : message honnête, aucun plan ouvert, et surtout aucune fenêtre
            // qui tombe. Le docteur, lui, avait déjà rendu son verdict sans réseau.
            _toasts.Show(ToastTone.Error, UiText.Mods.InstallFailedTitle, exception.Message);
        }
    }

    private async Task ApplyInstallAsync(ModInstallPlan plan, IReadOnlyCollection<string> selection)
    {
        try
        {
            var outcome = await _installService.ApplyAsync(_slug, plan, selection, cancellationToken: CancellationToken.None).ConfigureAwait(true);

            _overlay.Close();
            _toasts.Show(
                ToastTone.Success,
                UiText.Mods.InstalledTitle(plan.Primary.DisplayName),
                UiText.Mods.InstalledMessage(outcome.Installed.Count, _instanceName));
            InvalidateUpdateState();
            await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is DownloadFailedException or ModInstallFailedException)
        {
            _overlay.Close();
            _toasts.Show(ToastTone.Error, UiText.Mods.InstallFailedTitle, exception.Message);
        }
    }

    // La vérification inverse a lieu AVANT d'ouvrir la confirmation : elle en fournit le texte,
    // qui nomme les mods cassés plutôt que de servir un avertissement générique.
    private async Task RequestUninstallAsync(InstalledModRowViewModel row)
    {
        var impact = await _installService.PrepareUninstallAsync(_slug, row.Mod, CancellationToken.None).ConfigureAwait(true);

        _overlay.Show(new UninstallModDialogViewModel(impact, () => ConfirmUninstallAsync(row), _overlay));
    }

    private async Task ConfirmUninstallAsync(InstalledModRowViewModel row)
    {
        await _installService.UninstallAsync(_slug, row.Mod, CancellationToken.None).ConfigureAwait(true);
        _overlay.Close();
        _toasts.Show(ToastTone.Info, UiText.Mods.UninstalledTitle, row.Name);
        InvalidateUpdateState();
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
    }

    // Deux temps, comme l'installation : préparer (téléchargement + lecture du modinfo + plan),
    // puis appliquer ce que l'utilisateur a coché. Rien ne remplace le fichier avant confirmation.
    private async Task RequestUpdateAsync(InstalledModRowViewModel row)
    {
        if (row.UpdateResult is not { HasUpdate: true } updateResult)
        {
            return;
        }

        try
        {
            var plan = await _installService.PrepareUpdateAsync(_slug, updateResult, cancellationToken: CancellationToken.None).ConfigureAwait(true);

            _overlay.Show(new ModUpdatePlanDialogViewModel(plan, selection => ApplyUpdateAsync(row, plan, selection), _overlay));
        }
        catch (Exception exception) when (exception is ModDbApiException or ModDbUnavailableException or DownloadFailedException or ModInstallFailedException)
        {
            _toasts.Show(ToastTone.Error, UiText.Mods.UpdateFailedTitle, row.Name);
        }
    }

    private async Task ApplyUpdateAsync(InstalledModRowViewModel row, ModUpdatePlan plan, IReadOnlyCollection<string> selection)
    {
        try
        {
            await _installService.ApplyUpdateAsync(_slug, plan, selection, cancellationToken: CancellationToken.None).ConfigureAwait(true);

            _overlay.Close();
            _toasts.Show(ToastTone.Success, UiText.Mods.UpdatedTitle(row.Name), UiText.Mods.UpdatedMessage(plan.Updated.Version.ToString()));
            InvalidateUpdateState();
            await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is ModDbApiException or ModDbUnavailableException or DownloadFailedException or ModInstallFailedException)
        {
            _toasts.Show(ToastTone.Error, UiText.Mods.UpdateFailedTitle, row.Name);
        }
    }

    private ModUpdateResult? FindUpdateResult(InstalledMod mod)
        => _lastReport?.Mods.FirstOrDefault(result => string.Equals(result.Mod.FileName, mod.FileName, StringComparison.OrdinalIgnoreCase));

    private void ApplyReport(InstanceUpdateReport? report)
    {
        _lastReport = report;
        AvailableUpdateCount = report?.UpdateCount ?? 0;
        LastCheckedText = UiText.Mods.LastCheckedLabel(RelativeDateFormatter.Format(report?.CheckedUtc, _clock.UtcNow));
    }

    // Toute modification du dossier Mods (activation, retrait, mise à jour) rend le dernier résultat
    // potentiellement faux : mieux vaut ne plus rien affirmer que d'afficher un badge périmé, aussi
    // bien ici que sur la pastille de la carte d'Accueil (même règle, voir IModUpdateCheckCache).
    private void InvalidateUpdateState()
    {
        ApplyReport(null);
        _updateCache.Invalidate(_slug);
    }

    /// <summary>Libère le jeton d'annulation d'un éventuel « Tout mettre à jour » en cours.</summary>
    public void Dispose() => _updateAllCancellation?.Dispose();
}

/// <summary>
/// Une ligne de mod installé (design/components/launcher/ModRow.jsx) : icône extraite de l'archive,
/// nom, auteur, version, côté, badge de provenance, badge « non identifié » le cas échéant, et état
/// de mise à jour ModDB (à jour, mise à jour disponible, inconnu du ModDB, non identifiable).
/// </summary>
public sealed partial class InstalledModRowViewModel : ObservableObject
{
    private readonly Func<InstalledModRowViewModel, bool, Task> _toggle;
    private readonly Func<InstalledModRowViewModel, Task> _remove;
    private readonly Func<InstalledModRowViewModel, Task> _update;

    /// <summary>Construit la ligne.</summary>
    /// <param name="installedMod">Mod tel que vu par le dernier scan.</param>
    /// <param name="toggle">Bascule d'activation.</param>
    /// <param name="remove">Demande de désinstallation.</param>
    /// <param name="update">Demande de mise à jour.</param>
    /// <param name="updateResult">
    /// État de mise à jour connu pour ce mod, ou <see langword="null"/> si aucune vérification n'a
    /// encore eu lieu cette session.
    /// </param>
    public InstalledModRowViewModel(
        InstalledMod installedMod,
        Func<InstalledModRowViewModel, bool, Task> toggle,
        Func<InstalledModRowViewModel, Task> remove,
        Func<InstalledModRowViewModel, Task> update,
        ModUpdateResult? updateResult)
    {
        ArgumentNullException.ThrowIfNull(installedMod);
        ArgumentNullException.ThrowIfNull(toggle);
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(update);

        Mod = installedMod;
        _toggle = toggle;
        _remove = remove;
        _update = update;

        Name = installedMod.DisplayName;
        AuthorText = UiText.Mods.RowAuthor(installedMod.Info?.Authors ?? []);
        VersionText = installedMod.Version?.ToString() ?? UiText.Mods.UnknownVersion;
        SideText = UiText.Mods.SideLabel(installedMod.Info?.Side);
        HasSide = installedMod.Info is not null;
        FileNameText = installedMod.FileName;
        Icon = installedMod.Icon;

        IsFromModDb = installedMod.Provenance is not null;
        ProvenanceText = IsFromModDb ? UiText.Mods.ProvenanceModDb : UiText.Mods.ProvenanceManual;

        IsUnidentified = !installedMod.IsIdentified;
        UnidentifiedReason = UiText.Mods.UnidentifiedReason(installedMod.Problem);

        UpdateResult = updateResult;
        HasUpdateAvailable = updateResult?.HasUpdate ?? false;
        // « Non identifiable » recouvre exactement les mods déjà signalés IsUnidentified plus haut
        // (l'archive n'a pas pu être lue, la seule raison pour laquelle le vérificateur exclut un
        // mod de sa requête) : la maquette n'a donc rien de plus sobre à ajouter que ce badge déjà
        // affiché, pas de second indicateur redondant.
        IsUnknownToModDb = updateResult?.Status == ModUpdateStatus.UnknownToModDb;

        _isEnabled = installedMod.IsEnabled;
    }

    /// <summary>Mod sous-jacent, tel que rendu par le repository.</summary>
    public InstalledMod Mod { get; }

    public string Name { get; }

    public string AuthorText { get; }

    public string VersionText { get; }

    public string SideText { get; }

    public bool HasSide { get; }

    /// <summary>Nom du fichier sur le disque, en monospace.</summary>
    public string FileNameText { get; }

    /// <summary>Icône extraite de l'archive, ou <see langword="null"/>.</summary>
    public byte[]? Icon { get; }

    /// <summary>Vrai si Prospect a installé ce mod depuis le ModDB, faux s'il a été déposé à la main.</summary>
    public bool IsFromModDb { get; }

    public string ProvenanceText { get; }

    /// <summary>Vrai si l'archive n'a pas livré de <c>modinfo.json</c> exploitable.</summary>
    public bool IsUnidentified { get; }

    /// <summary>Raison lisible de la non-identification, vide quand le mod est identifié.</summary>
    public string UnidentifiedReason { get; }

    /// <summary>Résultat de la dernière vérification pour ce mod, ou <see langword="null"/> si aucune n'a eu lieu.</summary>
    public ModUpdateResult? UpdateResult { get; }

    /// <summary>Vrai si une mise à jour compatible est disponible : montre le badge ochre et le bouton « Mettre à jour ».</summary>
    public bool HasUpdateAvailable { get; }

    /// <summary>Vrai si la dernière vérification n'a pas pu confirmer que ce mod est connu du ModDB.</summary>
    public bool IsUnknownToModDb { get; }

    [ObservableProperty]
    private bool _isEnabled;

    // Le toggle est piloté par binding, pas par commande : c'est l'écriture de la propriété qui
    // déclenche le renommage. Aucun garde-fou de réentrance n'est nécessaire, parce que le
    // rafraîchissement qui suit reconstruit des lignes neuves plutôt que de réécrire celle-ci
    // (voir InstanceModsTabViewModel.RefreshAsync) : le disque reste la source de vérité.
    partial void OnIsEnabledChanged(bool value) => _ = _toggle(this, value);

    [RelayCommand]
    private Task RemoveAsync() => _remove(this);

    [RelayCommand]
    private Task UpdateAsync() => _update(this);
}