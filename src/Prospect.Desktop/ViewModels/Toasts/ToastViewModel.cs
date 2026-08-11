using CommunityToolkit.Mvvm.Input;

namespace Prospect.Desktop.ViewModels.Toasts;

/// <summary>Tonalité d'un toast, alignée sur les quatre variantes de <c>components/feedback/Toast.jsx</c>.</summary>
public enum ToastTone
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Un toast affiché par la pile de la fenêtre (voir <see cref="Prospect.Desktop.Services.IToastService"/>).
/// Immuable une fois créé : un toast ne change pas d'état, il disparaît (fermeture manuelle ou
/// délai écoulé), ce qui rend une classe simple suffisante, sans <c>ObservableObject</c>.
/// </summary>
public sealed class ToastViewModel
{
    public ToastViewModel(ToastTone tone, string title, string? description, Action<ToastViewModel> requestClose)
    {
        ArgumentNullException.ThrowIfNull(requestClose);
        ArgumentException.ThrowIfNullOrEmpty(title);

        Tone = tone;
        Title = title;
        Description = description;
        IconKey = tone switch
        {
            ToastTone.Success => "check-circle",
            ToastTone.Warning => "alert",
            ToastTone.Error => "alert",
            _ => "info",
        };
        CloseCommand = new RelayCommand(() => requestClose(this));
    }

    public ToastTone Tone { get; }

    public string Title { get; }

    public string? Description { get; }

    /// <summary>Clé résolue par <see cref="Prospect.Desktop.Converters.IconKeyToGeometryConverter"/>.</summary>
    public string IconKey { get; }

    public IRelayCommand CloseCommand { get; }
}