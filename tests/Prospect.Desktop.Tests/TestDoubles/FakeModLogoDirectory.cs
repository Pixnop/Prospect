using Prospect.Desktop.Services;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Faux <see cref="IModLogoDirectory"/> : une table d'identifiants vers URLs, posée par le test.
/// Le vrai annuaire lit le CACHE du client ModDB, ce qui obligerait chaque test de vignette à
/// relever d'abord un catalogue entier pour n'en tirer qu'une URL.
/// </summary>
internal sealed class FakeModLogoDirectory : IModLogoDirectory
{
    private readonly Dictionary<int, Uri> _logos;

    public FakeModLogoDirectory(params (int ModId, string Url)[] logos)
        => _logos = logos.ToDictionary(entry => entry.ModId, entry => new Uri(entry.Url));

    /// <summary>Identifiants demandés, dans l'ordre : de quoi vérifier QUI a une vignette et qui n'en demande pas.</summary>
    public List<int> RequestedModIds { get; } = [];

    /// <summary>Jeton reçu au dernier appel, pour vérifier qu'une rangée jetée annule bien sa recherche.</summary>
    public CancellationToken LastCancellationToken { get; private set; }

    public Task<Uri?> FindAsync(int modDbModId, CancellationToken cancellationToken = default)
    {
        RequestedModIds.Add(modDbModId);
        LastCancellationToken = cancellationToken;

        return Task.FromResult(_logos.GetValueOrDefault(modDbModId));
    }
}