using System.Collections.ObjectModel;

using Avalonia.Threading;

using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.Services;

/// <inheritdoc cref="IToastService" />
public sealed class ToastService : IToastService
{
    private static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    /// <inheritdoc />
    public void Show(ToastTone tone, string title, string? description = null)
    {
        var toast = new ToastViewModel(tone, title, description, Dismiss);
        Toasts.Add(toast);

        _ = AutoDismissAsync(toast);
    }

    private void Dismiss(ToastViewModel toast) => Toasts.Remove(toast);

    private async Task AutoDismissAsync(ToastViewModel toast)
    {
        await Task.Delay(DisplayDuration).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => Dismiss(toast));
    }
}