using Prospect.Core.Common;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Double de test d'<see cref="IExternalUrlOpener"/> : enregistre les URLs qu'on a voulu ouvrir,
/// sans jamais lancer de navigateur.
/// </summary>
internal sealed class FakeExternalUrlOpener : IExternalUrlOpener
{
    public List<Uri> Opened { get; } = [];

    /// <summary>Faux pour simuler l'absence de commande d'ouverture sur la machine.</summary>
    public bool Succeeds { get; set; } = true;

    public Task<bool> OpenAsync(Uri url, CancellationToken cancellationToken = default)
    {
        Opened.Add(url);

        return Task.FromResult(Succeeds);
    }
}