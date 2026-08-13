using System.Collections.ObjectModel;

using Avalonia.Threading;

using Prospect.Core.Common;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.Services;

/// <inheritdoc cref="IToastService" />
/// <remarks>
/// Les toasts d'ERREUR partent aussi dans le journal de diagnostic, et c'est le seul endroit du
/// projet où la couche interface y écrit. La raison est que ce sont les seuls échecs dont on sache
/// avec certitude que l'utilisateur les a VUS : c'est ce qu'il racontera dans son rapport, mot pour
/// mot, et sans cette ligne il n'y a rien à quoi rattacher son récit. Les autres tonalités ne sont
/// pas journalisées : une confirmation de succès n'apprend rien qu'un fait de domaine n'ait déjà
/// consigné, et un toast d'information encore moins.
/// </remarks>
public sealed class ToastService : IToastService
{
    private static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(5);

    private readonly IAppLog _log;

    /// <summary>Construit le service.</summary>
    /// <param name="log">Journal de diagnostic ; optionnel, voir la remarque d'<see cref="IAppLog"/>.</param>
    public ToastService(IAppLog? log = null) => _log = log ?? NullAppLog.Instance;

    /// <inheritdoc />
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    /// <inheritdoc />
    public void Show(ToastTone tone, string title, string? description = null)
    {
        var toast = new ToastViewModel(tone, title, description, Dismiss);
        Toasts.Add(toast);

        if (tone == ToastTone.Error)
        {
            _log.Write(
                AppLogLevel.Error,
                $"Erreur montrée à l'utilisateur : {title}{(string.IsNullOrWhiteSpace(description) ? string.Empty : $" : {description}")}");
        }

        _ = AutoDismissAsync(toast);
    }

    private void Dismiss(ToastViewModel toast) => Toasts.Remove(toast);

    private async Task AutoDismissAsync(ToastViewModel toast)
    {
        await Task.Delay(DisplayDuration).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => Dismiss(toast));
    }
}