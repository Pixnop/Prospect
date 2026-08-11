using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.ModDb;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Mods;

/// <summary>
/// Onglet Mods de la page de détail d'une instance (design/ui_kits/launcher/screen-instance.jsx et
/// composant <c>ModRow</c>) : liste des mods installés, activation, désinstallation avec
/// vérification inverse, et raccourci vers le navigateur préfiltré sur cette instance.
/// </summary>
public sealed partial class InstanceModsTabViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly IInstalledModRepository _repository;
    private readonly ModInstallService _installService;
    private readonly IOverlayService _overlay;
    private readonly IToastService _toasts;

    public InstanceModsTabViewModel(
        string slug,
        IInstalledModRepository repository,
        ModInstallService installService,
        IOverlayService overlay,
        IToastService toasts)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(installService);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toasts);

        _slug = slug;
        _repository = repository;
        _installService = installService;
        _overlay = overlay;
        _toasts = toasts;
        ModsDirectoryText = repository.GetModsDirectory(slug);
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
                Mods.Add(new InstalledModRowViewModel(mod, ToggleAsync, RequestUninstallAsync));
            }

            HasMods = Mods.Count > 0;
            SummaryText = UiText.Mods.InstalledSummary(Mods.Count, Mods.Count(row => row.IsEnabled));
        }
        finally
        {
            IsLoading = false;
        }
    }

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

        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
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
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
    }
}

/// <summary>
/// Une ligne de mod installé (design/components/launcher/ModRow.jsx) : icône extraite de l'archive,
/// nom, auteur, version, côté, badge de provenance et, le cas échéant, badge « non identifié » avec
/// sa raison.
/// </summary>
public sealed partial class InstalledModRowViewModel : ObservableObject
{
    private readonly Func<InstalledModRowViewModel, bool, Task> _toggle;
    private readonly Func<InstalledModRowViewModel, Task> _remove;

    /// <summary>Construit la ligne.</summary>
    /// <param name="installedMod">Mod tel que vu par le dernier scan.</param>
    /// <param name="toggle">Bascule d'activation.</param>
    /// <param name="remove">Demande de désinstallation.</param>
    public InstalledModRowViewModel(
        InstalledMod installedMod,
        Func<InstalledModRowViewModel, bool, Task> toggle,
        Func<InstalledModRowViewModel, Task> remove)
    {
        ArgumentNullException.ThrowIfNull(installedMod);
        ArgumentNullException.ThrowIfNull(toggle);
        ArgumentNullException.ThrowIfNull(remove);

        Mod = installedMod;
        _toggle = toggle;
        _remove = remove;

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

    [ObservableProperty]
    private bool _isEnabled;

    // Le toggle est piloté par binding, pas par commande : c'est l'écriture de la propriété qui
    // déclenche le renommage. Aucun garde-fou de réentrance n'est nécessaire, parce que le
    // rafraîchissement qui suit reconstruit des lignes neuves plutôt que de réécrire celle-ci
    // (voir InstanceModsTabViewModel.RefreshAsync) : le disque reste la source de vérité.
    partial void OnIsEnabledChanged(bool value) => _ = _toggle(this, value);

    [RelayCommand]
    private Task RemoveAsync() => _remove(this);
}