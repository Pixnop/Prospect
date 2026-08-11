using Prospect.Desktop.Services;

using Shouldly;

namespace Prospect.Desktop.Tests.Services;

/// <summary>
/// L'overlay possède le cycle de vie de ce qu'il affiche (voir la docstring d'<see cref="IOverlayService"/>) :
/// ces tests vérifient qu'un contenu <see cref="IDisposable"/> est bien disposé à la fermeture et
/// au remplacement, sans qu'un contenu non disposable ne pose problème.
/// </summary>
public class OverlayServiceTests
{
    private sealed class DisposableProbe : IDisposable
    {
        public bool WasDisposed { get; private set; }

        public void Dispose() => WasDisposed = true;
    }

    [Fact]
    public void Close_ActiveContentIsDisposable_DisposesIt()
    {
        var service = new OverlayService();
        var probe = new DisposableProbe();
        service.Show(probe);

        service.Close();

        probe.WasDisposed.ShouldBeTrue();
        service.Active.ShouldBeNull();
    }

    [Fact]
    public void Close_ActiveContentIsNotDisposable_DoesNotThrow()
    {
        var service = new OverlayService();
        service.Show(new object());

        Should.NotThrow(() => service.Close());
    }

    [Fact]
    public void Close_NoActiveContent_DoesNotThrow()
    {
        var service = new OverlayService();

        Should.NotThrow(() => service.Close());
    }

    [Fact]
    public void Show_ReplacesDisposableActiveContent_DisposesThePreviousOneOnly()
    {
        var service = new OverlayService();
        var first = new DisposableProbe();
        var second = new DisposableProbe();
        service.Show(first);

        service.Show(second);

        first.WasDisposed.ShouldBeTrue();
        second.WasDisposed.ShouldBeFalse();
        service.Active.ShouldBe(second);
    }

    [Fact]
    public void Show_SameInstanceAgain_DoesNotDisposeIt()
    {
        var service = new OverlayService();
        var probe = new DisposableProbe();
        service.Show(probe);

        service.Show(probe);

        probe.WasDisposed.ShouldBeFalse();
        service.Active.ShouldBe(probe);
    }
}