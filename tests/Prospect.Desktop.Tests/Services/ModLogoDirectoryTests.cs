using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;

using Shouldly;

namespace Prospect.Desktop.Tests.Services;

/// <summary>
/// L'annuaire des logos, adossé au SEUL cache du client ModDB. Ce qui se vérifie ici tient en une
/// phrase : il rend une URL quand le catalogue est déjà là, rien quand il ne l'est pas, et il ne
/// déclenche jamais de relevé.
/// </summary>
public sealed class ModLogoDirectoryTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment());
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FindAsync_SansCatalogueReleve_NeRendRienEtNInterrogePasLeReseau()
    {
        using var handler = new FakeModDbHandler();
        var directory = new ModLogoDirectory(CreateClient(new MockFileSystem(), handler));

        var logoUrl = await directory.FindAsync(792, CancellationToken.None);

        logoUrl.ShouldBeNull();
        handler.LogoRequestCount.ShouldBe(0);
    }

    /// <summary>
    /// Le cas nominal : le navigateur a relevé le catalogue plus tôt dans la session, et l'onglet
    /// Mods en profite sans rien redemander. Les deux écrans partagent le même client singleton.
    /// </summary>
    [Fact]
    public async Task FindAsync_CatalogueDejaReleve_RendLUrlDeLaFiche()
    {
        using var handler = new FakeModDbHandler();
        var client = CreateClient(new MockFileSystem(), handler);
        await client.GetCatalogAsync(cancellationToken: CancellationToken.None);
        var directory = new ModLogoDirectory(client);

        var logoUrl = await directory.FindAsync(792, CancellationToken.None);

        logoUrl.ShouldBe(new Uri("https://moddbcdn.vintagestory.at/betterruins.png"));
    }

    /// <summary>Un tiers du catalogue n'a pas de logo : ces fiches-là gardent le pictogramme.</summary>
    [Fact]
    public async Task FindAsync_FicheSansLogo_NeRendRien()
    {
        using var handler = new FakeModDbHandler();
        var client = CreateClient(new MockFileSystem(), handler);
        await client.GetCatalogAsync(cancellationToken: CancellationToken.None);

        (await new ModLogoDirectory(client).FindAsync(1783, CancellationToken.None)).ShouldBeNull();
    }

    /// <summary>
    /// Une liste de vingt mods construite d'un coup ne doit demander le catalogue qu'UNE fois : la
    /// table se bâtit derrière un <c>Lazy</c>, les autres appelants attendent la même tâche.
    /// </summary>
    [Fact]
    public async Task FindAsync_PlusieursRangeesDUnCoup_NeConstruitLaTableQuUneFois()
    {
        var fileSystem = new MockFileSystem();
        using var handler = new FakeModDbHandler();
        var client = new CountingModDbClient(CreateClient(fileSystem, handler));
        await client.Inner.GetCatalogAsync(cancellationToken: CancellationToken.None);
        var directory = new ModLogoDirectory(client);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => directory.FindAsync(792, CancellationToken.None)));

        results.ShouldAllBe(url => url != null);
        client.CachedCatalogReads.ShouldBe(1);
    }

    /// <summary>
    /// Un mod sans provenance arrive ici avec un identifiant nul, que l'appelant traduit en zéro :
    /// aucune raison d'aller construire une table pour ça.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task FindAsync_IdentifiantNonExploitable_CourtCircuiteSansToucherAuCatalogue(int modDbModId)
    {
        var client = new CountingModDbClient(CreateClient(new MockFileSystem(), new FakeModDbHandler()));

        (await new ModLogoDirectory(client).FindAsync(modDbModId, CancellationToken.None)).ShouldBeNull();

        client.CachedCatalogReads.ShouldBe(0);
    }

    /// <summary>
    /// Une rangée jetée annule sa recherche, et cette annulation ne doit pas empoisonner la table
    /// des autres rangées : le jeton de l'appelant n'entre jamais dans la construction partagée.
    /// </summary>
    [Fact]
    public async Task FindAsync_AnnulationDUnAppelant_NEmpoisonnePasLaTablePartagee()
    {
        using var handler = new FakeModDbHandler();
        var inner = CreateClient(new MockFileSystem(), handler);
        await inner.GetCatalogAsync(cancellationToken: CancellationToken.None);

        // La table est retenue en construction : c'est le seul moment où l'annulation d'un
        // appelant PEUT emporter celle des autres, donc le seul moment où le test a du sens.
        var gate = new TaskCompletionSource();
        var directory = new ModLogoDirectory(new CountingModDbClient(inner, gate.Task));

        using var canceled = new CancellationTokenSource();
        var abandoned = directory.FindAsync(792, canceled.Token);
        var patient = directory.FindAsync(792, CancellationToken.None);
        await canceled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => abandoned);

        gate.SetResult();
        (await patient).ShouldBe(new Uri("https://moddbcdn.vintagestory.at/betterruins.png"));
        (await directory.FindAsync(792, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public void Constructeur_RefuseUnClientNul()
        => Should.Throw<ArgumentNullException>(() => new ModLogoDirectory(null!));

    private static ModDbClient CreateClient(MockFileSystem fileSystem, HttpMessageHandler handler)
        => new(
            new HttpClient(handler, disposeHandler: false),
            new JsonFileStore(fileSystem),
            Paths,
            new FakeClock(Noon),
            new Core.Http.RetryPolicy(Core.Http.RetryOptions.NoDelay, (_, _) => Task.CompletedTask));

    /// <summary>Compte les lectures de cache, la seule chose que l'annuaire ait le droit de demander.</summary>
    private sealed class CountingModDbClient : IModDbClient
    {
        private readonly Task? _gate;

        public CountingModDbClient(IModDbClient inner, Task? gate = null)
        {
            Inner = inner;
            _gate = gate;
        }

        public IModDbClient Inner { get; }

        public int CachedCatalogReads { get; private set; }

        public Task<ModDbCatalog> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
            => Inner.GetCatalogAsync(forceRefresh, cancellationToken);

        public async Task<ModDbCatalog?> TryGetCachedCatalogAsync(CancellationToken cancellationToken = default)
        {
            CachedCatalogReads++;
            if (_gate is not null)
            {
                await _gate.ConfigureAwait(false);
            }

            return await Inner.TryGetCachedCatalogAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<IReadOnlyList<ModDbTag>> GetTagsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
            => Inner.GetTagsAsync(forceRefresh, cancellationToken);

        public Task<ModDbModDetail> GetModAsync(string modIdOrIdentifier, CancellationToken cancellationToken = default)
            => Inner.GetModAsync(modIdOrIdentifier, cancellationToken);

        public Task<ModDbCompatibilityIndex> GetCompatibilityIndexAsync(
            Core.Common.GameVersion gameVersion,
            bool widenToMinorSeries = false,
            CancellationToken cancellationToken = default)
            => Inner.GetCompatibilityIndexAsync(gameVersion, widenToMinorSeries, cancellationToken);

        public Task<IReadOnlyDictionary<string, ModDbInstallInformation>> GetInstallInformationAsync(
            IReadOnlyList<string> identifiers,
            Core.Common.GameVersion gameVersion,
            CancellationToken cancellationToken = default)
            => Inner.GetInstallInformationAsync(identifiers, gameVersion, cancellationToken);

        public Task<long?> GetFileSizeAsync(Uri fileUrl, CancellationToken cancellationToken = default)
            => Inner.GetFileSizeAsync(fileUrl, cancellationToken);

        public Task<IReadOnlyDictionary<string, ModDbRelease>> GetUpdatesAsync(
            IReadOnlyDictionary<string, Core.Common.ModVersion> installedMods,
            CancellationToken cancellationToken = default)
            => Inner.GetUpdatesAsync(installedMods, cancellationToken);
    }
}