using CommunityToolkit.Mvvm.ComponentModel;

namespace Prospect.Desktop.Services;

/// <inheritdoc cref="IOverlayService" />
public sealed partial class OverlayService : ObservableObject, IOverlayService
{
    [ObservableProperty]
    private object? _active;

    /// <inheritdoc />
    public void Show(object overlayViewModel)
    {
        ArgumentNullException.ThrowIfNull(overlayViewModel);

        if (!ReferenceEquals(Active, overlayViewModel))
        {
            DisposeIfDisposable(Active);
        }

        Active = overlayViewModel;
    }

    /// <inheritdoc />
    public void Close()
    {
        var previous = Active;
        Active = null;
        DisposeIfDisposable(previous);
    }

    private static void DisposeIfDisposable(object? content)
    {
        if (content is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}