using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Instances;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Dialogs;

/// <summary>
/// Dialogue « Dupliquer » ouvert depuis le menu d'une carte d'instance : un champ pour le nom de
/// la copie, puis une barre de progression annulable branchée sur
/// <see cref="InstanceService.DuplicateAsync"/> et <see cref="InstanceCopyProgress"/>. Les deux
/// phases (saisie, copie en cours) vivent dans le même ViewModel plutôt que deux dialogues
/// distincts : c'est une seule conversation avec l'utilisateur, pas deux dialogues indépendants.
/// </summary>
public sealed partial class DuplicateDialogViewModel : ObservableObject, IDisposable
{
    private readonly string _sourceSlug;
    private readonly InstanceService _instanceService;
    private readonly IOverlayService _overlay;
    private readonly Func<Task> _requestRefresh;
    private CancellationTokenSource? _cancellation;

    public DuplicateDialogViewModel(
        string sourceSlug,
        string sourceName,
        InstanceService instanceService,
        IOverlayService overlay,
        Func<Task> requestRefresh)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceSlug);
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(requestRefresh);

        _sourceSlug = sourceSlug;
        _instanceService = instanceService;
        _overlay = overlay;
        _requestRefresh = requestRefresh;
        _name = UiText.Dialogs.DuplicateSuggestedName(sourceName);
        _progressLabel = string.Empty;
        Title = UiText.Dialogs.DuplicateTitle(sourceName);
    }

    /// <summary>Titre du dialogue : une question qui nomme l'instance SOURCE, pas la copie à venir.</summary>
    public string Title { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputPhase))]
    private bool _isInProgress;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressLabel;

    [ObservableProperty]
    private string? _failureMessage;

    public string? Error => string.IsNullOrWhiteSpace(Name) ? UiText.Dialogs.DuplicateEmptyError : null;

    public bool IsValid => Error is null;

    /// <summary>Formulaire de saisie du nom, par opposition à la barre de progression (<see cref="IsInProgress"/>).</summary>
    public bool IsInputPhase => !IsInProgress;

    [RelayCommand]
    private void Cancel()
    {
        if (IsInProgress)
        {
            _cancellation?.Cancel();
            return;
        }

        _overlay.Close();
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private async Task ConfirmAsync()
    {
        FailureMessage = null;
        IsInProgress = true;
        ProgressPercent = 0;
        ProgressLabel = UiText.Dialogs.DuplicateProgressLabel(0, 0);

        _cancellation = new CancellationTokenSource();
        var progress = new Progress<InstanceCopyProgress>(report =>
        {
            ProgressPercent = report.TotalFiles == 0 ? 0 : report.FilesCopied * 100d / report.TotalFiles;
            ProgressLabel = UiText.Dialogs.DuplicateProgressLabel(report.FilesCopied, report.TotalFiles);
        });

        try
        {
            await _instanceService.DuplicateAsync(_sourceSlug, Name.Trim(), progress, _cancellation.Token).ConfigureAwait(true);
            _overlay.Close();
            await _requestRefresh().ConfigureAwait(true);
            return;
        }
        catch (OperationCanceledException)
        {
            // Annulation demandée par l'utilisateur (voir Cancel()) : retour au formulaire, sans
            // message d'échec (ce n'est pas une erreur).
            FailureMessage = null;
        }
        catch (Exception ex) when (ex is InstanceNotFoundException or InstanceNameInvalidException)
        {
            FailureMessage = ex.Message;
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            IsInProgress = false;
        }
    }

    /// <summary>Libère le <see cref="CancellationTokenSource"/> d'une copie en cours si le dialogue est fermé pendant celle-ci (voir CA1001).</summary>
    public void Dispose() => _cancellation?.Dispose();
}