using Prospect.Core.Auth;

namespace Prospect.Core.Tests.Auth;

/// <summary>
/// Double d'<see cref="ISecretStore"/> en mémoire : garde ce qu'on lui confie et compte les
/// effacements, sans jamais toucher au disque. Sert aussi de garde de discipline — tout ce que le
/// service persiste passe par ici et reste inspectable, donc vérifiable.
/// </summary>
internal sealed class FakeSecretStore : ISecretStore
{
    public VsSession? Stored { get; set; }

    public int SaveCount { get; private set; }

    public int ClearCount { get; private set; }

    public Task<VsSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Stored);

    public Task SaveAsync(VsSession session, CancellationToken cancellationToken = default)
    {
        Stored = session;
        SaveCount++;

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Stored = null;
        ClearCount++;

        return Task.CompletedTask;
    }
}
