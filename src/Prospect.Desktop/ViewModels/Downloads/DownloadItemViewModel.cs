using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Http;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Downloads;

/// <summary>
/// Une ligne du popover Téléchargements (design/components/launcher/DownloadItem.jsx) : nom,
/// avancement, débit, bouton d'annulation. Vue mince sur une <see cref="DownloadOperation"/> du
/// Core, dont elle suit les évènements.
/// </summary>
public sealed partial class DownloadItemViewModel : ObservableObject, IDisposable
{
    private readonly DownloadOperation _operation;
    private readonly IUiDispatcher _dispatcher;

    public DownloadItemViewModel(DownloadOperation operation, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _operation = operation;
        _dispatcher = dispatcher;
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

    /// <summary>Icône de la ligne : sablier tant que le téléchargement attend son tour, flèche dès qu'il démarre.</summary>
    public string IconKey => IsQueued ? "clock" : "download";

    [RelayCommand]
    private void Cancel() => _operation.Cancel();

    public void Dispose() => _operation.Changed -= OnOperationChanged;

    private void OnOperationChanged(object? sender, EventArgs e) => _dispatcher.Post(Refresh);

    private void Refresh()
    {
        var progress = _operation.Progress;
        var state = _operation.State;

        IsQueued = state == DownloadState.Queued;
        IsFailed = state == DownloadState.Failed;
        IsIndeterminate = state == DownloadState.Verifying || (state == DownloadState.Running && progress.Ratio is null);

        StatText = state switch
        {
            DownloadState.Queued => UiText.Downloads.Queued,
            DownloadState.Verifying => UiText.Downloads.Verifying,
            _ => ByteSizeFormatter.FormatProgress(progress.ReceivedBytes, progress.TotalBytes),
        };

        SpeedText = IsFailed
            ? _operation.FailureMessage ?? UiText.Downloads.GenericFailure
            : ByteSizeFormatter.FormatSpeed(progress.BytesPerSecond);

        ProgressPercent = (progress.Ratio ?? 0d) * 100d;
        PercentText = progress.Ratio is { } ratio ? $"{ratio * 100d:0} %" : string.Empty;

        OnPropertyChanged(nameof(IconKey));
    }
}