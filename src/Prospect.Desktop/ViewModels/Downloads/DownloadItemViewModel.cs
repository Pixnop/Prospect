using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.Http;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Downloads;

/// <summary>
/// Une ligne du popover Téléchargements (design/components/launcher/DownloadItem.jsx) : nom,
/// avancement, débit, bouton d'annulation. Vue mince sur une <see cref="DownloadOperation"/> du
/// Core, dont elle suit les évènements.
///
/// La même ligne sert vivante et terminée. Une opération finie garde sa place dans la file (voir
/// <see cref="IDownloadManager.Operations"/>), et c'est ici que sa présentation bascule : la barre
/// et le débit cèdent la place à un état, une taille et une heure relative, et la croix
/// d'annulation devient une croix de retrait.
/// </summary>
public sealed partial class DownloadItemViewModel : ObservableObject, IDisposable
{
    private readonly DownloadOperation _operation;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClock _clock;
    private readonly Action<DownloadOperation> _dismiss;

    public DownloadItemViewModel(
        DownloadOperation operation,
        IUiDispatcher dispatcher,
        IClock clock,
        Action<DownloadOperation> dismiss)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(dismiss);

        _operation = operation;
        _dispatcher = dispatcher;
        _clock = clock;
        _dismiss = dismiss;
        _operation.Changed += OnOperationChanged;
        Refresh();
    }

    /// <summary>Opération sous-jacente, exposée pour que la liste reconnaisse ses éléments existants.</summary>
    public DownloadOperation Operation => _operation;

    public string Name => _operation.DisplayName;

    [ObservableProperty]
    private string _statText = string.Empty;

    [ObservableProperty]
    private string _speedText = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _percentText = string.Empty;

    [ObservableProperty]
    private bool _isQueued;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private bool _isIndeterminate;

    /// <summary>Vrai dès que l'opération est terminée, quelle qu'en soit l'issue.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private bool _isFinished;

    /// <summary>Complément d'<see cref="IsFinished"/>, pour les liaisons qui montrent la barre.</summary>
    public bool IsActive => !IsFinished;

    /// <summary>« terminé », « échec », « annulé » — le verdict d'une ligne d'historique.</summary>
    [ObservableProperty]
    private string _outcomeText = string.Empty;

    /// <summary>Ton du badge de verdict : `stable`, `incompatible` ou vide.</summary>
    [ObservableProperty]
    private string _outcomeTone = string.Empty;

    /// <summary>Heure de fin en relatif (« il y a 3 minutes »), vide tant que l'opération vit.</summary>
    [ObservableProperty]
    private string _finishedText = string.Empty;

    /// <summary>Icône de la ligne : sablier tant que le téléchargement attend son tour, flèche dès qu'il démarre.</summary>
    public string IconKey => (IsFinished, IsFailed, IsQueued) switch
    {
        (true, true, _) => "alert",
        (true, false, _) => "check",
        (false, _, true) => "clock",
        _ => "download",
    };

    /// <summary>
    /// Une seule croix pour deux gestes, parce que c'est le même du point de vue de l'utilisateur :
    /// « enlève-moi cette ligne ». Sur un téléchargement vivant, elle l'annule ; sur une ligne
    /// d'historique, elle la retire.
    /// </summary>
    [RelayCommand]
    private void Dismiss()
    {
        if (_operation.IsFinished)
        {
            _dismiss(_operation);

            return;
        }

        _operation.Cancel();
    }

    public void Dispose() => _operation.Changed -= OnOperationChanged;

    /// <summary>
    /// Recalcule l'heure relative. Rien ne bat la seconde dans ce popover : le texte est repris
    /// quand l'utilisateur l'ouvre, ce qui suffit à un historique de session et évite un
    /// minuteur qui tournerait pour un panneau fermé.
    /// </summary>
    internal void RefreshElapsed()
    {
        FinishedText = _operation.FinishedUtc is { } finished
            ? RelativeDateFormatter.FormatMoment(finished, _clock.UtcNow)
            : string.Empty;
    }

    private void OnOperationChanged(object? sender, EventArgs e) => _dispatcher.Post(Refresh);

    private void Refresh()
    {
        var progress = _operation.Progress;
        var state = _operation.State;

        IsQueued = state == DownloadState.Queued;
        IsFailed = state == DownloadState.Failed;
        IsFinished = _operation.IsFinished;
        IsIndeterminate = state == DownloadState.Verifying || (state == DownloadState.Running && progress.Ratio is null);

        StatText = state switch
        {
            DownloadState.Queued => UiText.Downloads.Queued,
            DownloadState.Verifying => UiText.Downloads.Verifying,
            // Terminé : la taille reçue suffit, le total n'apprendrait plus rien.
            DownloadState.Completed => ByteSizeFormatter.Format(_operation.ReceivedBytes),
            _ => ByteSizeFormatter.FormatProgress(progress.ReceivedBytes, progress.TotalBytes),
        };

        SpeedText = IsFailed
            ? _operation.FailureMessage ?? UiText.Downloads.GenericFailure
            : ByteSizeFormatter.FormatSpeed(progress.BytesPerSecond);

        (OutcomeText, OutcomeTone) = state switch
        {
            DownloadState.Completed => (UiText.Downloads.OutcomeCompleted, "stable"),
            DownloadState.Failed => (UiText.Downloads.OutcomeFailed, "incompatible"),
            DownloadState.Canceled => (UiText.Downloads.OutcomeCanceled, string.Empty),
            _ => (string.Empty, string.Empty),
        };

        ProgressPercent = (progress.Ratio ?? 0d) * 100d;
        PercentText = progress.Ratio is { } ratio ? $"{ratio * 100d:0} %" : string.Empty;

        RefreshElapsed();
        OnPropertyChanged(nameof(IconKey));
    }
}