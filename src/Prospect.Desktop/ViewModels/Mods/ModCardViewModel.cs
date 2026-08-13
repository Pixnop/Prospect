using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.ModDb;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Mods;

/// <summary>
/// Une carte de résultat du navigateur de mods (design/ui_kits/launcher/screen-mods.jsx,
/// composant <c>ModCard</c>) : logo ou pictogramme générique, nom, auteur, téléchargements,
/// résumé, badges de compatibilité et de côté, bouton Installer.
/// </summary>
public sealed partial class ModCardViewModel : ObservableObject, IDisposable
{
    private readonly Func<ModCardViewModel, Task> _open;
    private readonly Func<ModCardViewModel, Task> _install;
    private readonly CancellationTokenSource _logoCancellation = new();

    private bool _disposed;
    private readonly Task _logoLoadTask = Task.CompletedTask;

    /// <summary>Construit la carte.</summary>
    /// <param name="summary">Entrée de catalogue.</param>
    /// <param name="compatibility">État de compatibilité vis-à-vis de l'instance cible.</param>
    /// <param name="open">Ouvre la fiche détaillée.</param>
    /// <param name="install">Lance la préparation d'installation.</param>
    /// <param name="logoCache">
    /// Cache de logos, interrogé seulement si <paramref name="summary"/> en annonce un
    /// (<c>docs/research/moddb-api.md</c> : absent sur 30 à 40 % du catalogue).
    /// </param>
    /// <param name="installed">
    /// Le mod déjà présent dans l'instance ciblée, ou <see langword="null"/>. Quand il l'est, la
    /// carte le DIT et son bouton change de verbe : « Gérer » ouvre la fiche, là où « Installer »
    /// relançait un téléchargement à l'aveugle sur quelque chose qui était déjà là.
    /// </param>
    public ModCardViewModel(
        ModDbModSummary summary,
        ModCompatibilityBadge compatibility,
        InstalledModMatch? installed,
        Func<ModCardViewModel, Task> open,
        Func<ModCardViewModel, Task> install,
        IModLogoCache logoCache)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(logoCache);

        Summary = summary;
        _open = open;
        _install = install;

        Name = HtmlText.DecodeEntities(summary.Name);
        AuthorText = UiText.Mods.ByAuthor(summary.Author);
        DownloadsText = UiText.Mods.FormatCount(summary.Downloads);
        Description = HtmlText.DecodeEntities(summary.Summary);
        HasDescription = Description.Length > 0;
        SideText = UiText.Mods.SideLabel(summary.Side);
        HasSide = summary.Side != ModDbSide.Unknown;
        _compatibility = compatibility;
        _installed = installed;

        if (summary.LogoUrl is { } logoUrl)
        {
            _logoLoadTask = LoadLogoAsync(logoCache, logoUrl);
        }
    }

    /// <summary>Entrée de catalogue derrière cette carte.</summary>
    public ModDbModSummary Summary { get; }

    /// <summary>Identifiant numérique de la fiche.</summary>
    public int ModId => Summary.ModId;

    public string Name { get; }

    public string AuthorText { get; }

    public string DownloadsText { get; }

    /// <summary>Résumé court du catalogue, décodé de ses entités HTML. La vue le tronque à deux lignes.</summary>
    public string Description { get; }

    /// <summary>Faux quand le catalogue ne fournit aucun résumé : la vue masque alors la ligne entière plutôt que de réserver un blanc.</summary>
    public bool HasDescription { get; }

    public string SideText { get; }

    public bool HasSide { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    private Bitmap? _logoBitmap;

    /// <summary>Vrai une fois le logo décodé avec succès : la vue bascule alors du pictogramme générique vers l'image.</summary>
    public bool HasLogo => LogoBitmap is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompatibilityText))]
    [NotifyPropertyChangedFor(nameof(CompatibilityTone))]
    [NotifyPropertyChangedFor(nameof(ShowCompatibility))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private ModCompatibilityBadge _compatibility;

    /// <summary>Libellé du badge de version, vide quand aucune instance cible n'est choisie.</summary>
    public string CompatibilityText => Compatibility.Text;

    /// <summary>Ton du badge, aligné sur le vocabulaire de canaux du design.</summary>
    public string CompatibilityTone => Compatibility.Tone;

    public bool ShowCompatibility => Compatibility.IsVisible;

    /// <summary>
    /// Le mod déjà installé dans l'instance ciblée, ou <see langword="null"/>. Change quand
    /// l'utilisateur change de cible et après une installation faite depuis le navigateur.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled))]
    [NotifyPropertyChangedFor(nameof(InstalledText))]
    [NotifyPropertyChangedFor(nameof(ActionLabel))]
    private InstalledModMatch? _installed;

    /// <summary>Vrai quand ce mod est déjà dans l'instance ciblée par le sélecteur.</summary>
    public bool IsInstalled => Installed is not null;

    /// <summary>« Installé · 1.2.0 », ou vide.</summary>
    public string InstalledText => Installed is { } match ? UiText.Mods.InstalledBadge(match.Version?.ToString()) : string.Empty;

    /// <summary>
    /// Verbe du bouton principal. « Gérer » sur un mod déjà là : la fiche est l'endroit où vivent le
    /// sélecteur de release et le remplacement, donc l'endroit où l'on CHANGE de version. Relancer
    /// « Installer » sur un mod présent était un téléchargement à l'aveugle.
    /// </summary>
    public string ActionLabel => IsInstalled ? UiText.Mods.ManageAction : UiText.Mods.InstallAction;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private Task OpenAsync() => _open(this);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync() => IsInstalled ? _open(this) : _install(this);

    private bool CanInstall() => !IsBusy;

    partial void OnIsBusyChanged(bool value) => InstallCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Le chargement de logo en vol, si la carte en a un à charger (sinon <see cref="Task.CompletedTask"/>).
    /// Jamais attendu en production : exposé en <see langword="internal"/> pour que les tests
    /// headless puissent attendre la fin du chargement asynchrone avant d'inspecter le rendu, sans
    /// sondage arbitraire (voir tests/Prospect.Desktop.Tests, <c>InternalsVisibleTo</c>).
    /// </summary>
    internal Task LogoLoadCompletion => _logoLoadTask;

    // Jamais bloquant pour la construction de la carte, jamais d'exception qui remonterait jusqu'à
    // ModBrowserViewModel.Rebuild : IModLogoCache rend déjà null pour tout échec réseau ou de
    // décodage, seule l'annulation volontaire (carte remplacée avant la fin, voir Dispose) reste à
    // absorber ici.
    private async Task LoadLogoAsync(IModLogoCache logoCache, Uri logoUrl)
    {
        try
        {
            LogoBitmap = await logoCache.GetAsync(logoUrl, _logoCancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Annule un chargement de logo encore en vol. <see cref="ModBrowserViewModel"/> reconstruit
    /// entièrement <c>Results</c> à chaque frappe de recherche : sans ce <see cref="Dispose"/>,
    /// taper vite laisserait une traînée de téléchargements de logos pour des cartes déjà jetées.
    /// Ne dispose PAS <see cref="LogoBitmap"/> : le bitmap appartient au cache
    /// (<see cref="IModLogoCache"/>), qui peut le servir à d'autres cartes pour la même URL.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logoCancellation.Cancel();
        _logoCancellation.Dispose();
    }
}

/// <summary>
/// Ce qu'affiche le badge de compatibilité d'une carte. Trois états seulement, parce que l'API ne
/// permet pas d'en distinguer davantage sans ouvrir la fiche de chaque mod : compatible,
/// incompatible, ou pas d'avis (aucune instance cible choisie, ou index de compatibilité
/// indisponible parce que le ModDB n'a pas répondu).
/// </summary>
/// <param name="Text">Libellé, généralement la version de jeu visée.</param>
/// <param name="Tone">Classe de ton du badge : <c>stable</c>, <c>incompatible</c> ou vide.</param>
/// <param name="IsVisible">Faux quand il n'y a rien à affirmer.</param>
/// <param name="IsCompatible">Vrai si le mod a une release taguée pour la version visée.</param>
public sealed record ModCompatibilityBadge(string Text, string Tone, bool IsVisible, bool IsCompatible)
{
    /// <summary>Aucun avis : pas d'instance cible, ou index indisponible.</summary>
    public static ModCompatibilityBadge None { get; } = new(string.Empty, string.Empty, IsVisible: false, IsCompatible: true);

    /// <summary>Construit le badge pour une version de jeu et un verdict.</summary>
    public static ModCompatibilityBadge For(string gameVersionText, bool isCompatible)
        => new(gameVersionText, isCompatible ? "stable" : "incompatible", IsVisible: true, isCompatible);
}