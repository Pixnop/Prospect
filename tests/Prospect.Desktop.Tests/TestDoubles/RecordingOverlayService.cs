using Prospect.Desktop.Services;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>Double de test d'<see cref="IOverlayService"/> : garde le même comportement (un panneau actif à la fois) et journalise chaque appel à <see cref="Show"/>.</summary>
internal sealed class RecordingOverlayService : IOverlayService
{
    public List<object> Shown { get; } = [];

    public object? Active { get; private set; }

    public void Show(object overlayViewModel)
    {
        Shown.Add(overlayViewModel);
        Active = overlayViewModel;
    }

    public void Close() => Active = null;
}