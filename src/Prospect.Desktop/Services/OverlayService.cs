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

        Active = overlayViewModel;
    }

    /// <inheritdoc />
    public void Close() => Active = null;
}