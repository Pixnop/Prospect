using System.Collections.ObjectModel;

using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>Double de test d'<see cref="IToastService"/> : journalise chaque appel à <see cref="Show"/> sans planifier de fermeture automatique.</summary>
internal sealed class RecordingToastService : IToastService
{
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public List<(ToastTone Tone, string Title, string? Description)> Shown { get; } = [];

    public void Show(ToastTone tone, string title, string? description = null) => Shown.Add((tone, title, description));
}