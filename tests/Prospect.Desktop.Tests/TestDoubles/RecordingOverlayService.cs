using Prospect.Desktop.Services;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>Double de test d'<see cref="IOverlayService"/> : garde le même comportement (un panneau actif à la fois) et journalise chaque appel à <see cref="Show"/>.</summary>
/// <remarks>
/// Y compris la POSSESSION du cycle de vie : un panneau <see cref="IDisposable"/> est disposé quand
/// il cesse d'être actif, exactement comme le fait <see cref="OverlayService"/>. Ce double l'avait
/// omis, et c'est ce qui a laissé passer le plantage de fermeture de fiche : un double plus
/// indulgent que le vrai service ne garde rien.
/// </remarks>
internal sealed class RecordingOverlayService : IOverlayService
{
    public List<object> Shown { get; } = [];

    public object? Active { get; private set; }

    public void Show(object overlayViewModel)
    {
        ArgumentNullException.ThrowIfNull(overlayViewModel);

        Shown.Add(overlayViewModel);

        if (!ReferenceEquals(Active, overlayViewModel))
        {
            DisposeIfDisposable(Active);
        }

        Active = overlayViewModel;
    }

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