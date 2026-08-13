using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Http;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Versions;

/// <summary>
/// Une ligne de l'écran Versions (design/ui_kits/launcher/screen-versions.jsx, <c>VersionRow</c>) :
/// version, badge de canal, taille, et selon l'état soit le bouton d'installation, soit la
/// progression annulable, soit la désinstallation. Le travail réel est fait par
/// <see cref="GameInstallService"/> ; cette ligne l'appelle et reflète son avancement.
/// </summary>
public sealed partial class GameVersionRowViewModel : ObservableObject, IProgress<GameInstallProgress>
{
    private readonly GameInstallService _installService;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<GameVersionRowViewModel, Task> _onInstalled;
    private readonly Func<GameVersionRowViewModel, Task> _onUninstallRequested;

    private CancellationTokenSource? _installCancellation;

    public GameVersionRowViewModel(
        GameVersion version,
        string sizeText,
        string metaText,
        bool isInstalled,
        GameInstallService installService,
        IUiDispatcher dispatcher,
        Func<GameVersionRowViewModel, Task> onInstalled,
        Func<GameVersionRowViewModel, Task> onUninstallRequested)
    {
        ArgumentNullException.ThrowIfNull(installService);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(onInstalled);
        ArgumentNullException.ThrowIfNull(onUninstallRequested);

        Version = version;
        VersionText = version.ToString();
        ChannelBadgeTone = ChannelBadgePresentation.ToBadgeTone(version.Channel);
        SizeText = sizeText;
        MetaText = metaText;
        _isInstalled = isInstalled;
        _installService = installService;
        _dispatcher = dispatcher;
        _onInstalled = onInstalled;
        _onUninstallRequested = onUninstallRequested;
    }

    public GameVersion Version { get; }

    public string VersionText { get; }

    /// <summary>Teinte de badge du design (<c>stable</c>, <c>unstable</c>, <c>pre</c>).</summary>
    public string ChannelBadgeTone { get; }

    /// <summary>Taille annoncée par le catalogue, ou taille sur disque pour une version installée.</summary>
    public string SizeText { get; }

    /// <summary>Ligne secondaire : date d'installation ou nom du fichier à télécharger.</summary>
    public string MetaText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isWorking;

    [ObservableProperty]
    private double _progressPercent;

    /// <summary>Libellé de phase : « Téléchargement », « Vérification », « Installation ».</summary>
    [ObservableProperty]
    private string _phaseText = string.Empty;

    /// <summary>Détail machine de la phase courante : octets reçus et débit.</summary>
    [ObservableProperty]
    private string _phaseDetailText = string.Empty;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private string? _errorMessage;

    public bool CanInstall => !IsInstalled && !IsWorking;

    /// <inheritdoc />
    public void Report(GameInstallProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _dispatcher.Post(() => Apply(value));
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        var cancellation = new CancellationTokenSource();
        _installCancellation = cancellation;
        IsWorking = true;
        ErrorMessage = null;
        Apply(GameInstallProgress.ForPhase(GameInstallPhase.Downloading));

        try
        {
            await _installService.InstallAsync(Version, this, cancellation.Token).ConfigureAwait(true);
            IsInstalled = true;
            await _onInstalled(this).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = null;
        }
        catch (GameInstallIncompleteException exception)
        {
            // Le seul échec d'installation dont le message du domaine ne suffit pas : ce qui compte
            // pour l'utilisateur n'est pas « aucun exécutable attendu », c'est OÙ Prospect
            // l'attendait et pourquoi il n'y est pas. Traduit, donc, contrairement aux autres.
            ErrorMessage = UiText.Versions.InstallLandedElsewhere(exception.TargetDirectory);
        }
        catch (Exception exception) when (exception is GameVersionNotAvailableException or GameInstallFailedException or DownloadFailedException)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsWorking = false;
            PhaseText = string.Empty;
            PhaseDetailText = string.Empty;
            ProgressPercent = 0d;
            _installCancellation = null;
            cancellation.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(IsWorking))]
    private void Cancel() => _installCancellation?.Cancel();

    [RelayCommand]
    private Task UninstallAsync() => _onUninstallRequested(this);

    private void Apply(GameInstallProgress progress)
    {
        PhaseText = UiText.Versions.PhaseLabel(progress.Phase);
        IsIndeterminate = progress.Ratio is null;
        ProgressPercent = (progress.Ratio ?? 0d) * 100d;
        PhaseDetailText = GameInstallProgressPresenter.DetailText(progress);
    }
}