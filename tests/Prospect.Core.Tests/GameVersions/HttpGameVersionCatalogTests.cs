using System.IO.Abstractions.TestingHelpers;
using System.Net;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Http;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Http;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.GameVersions;

public sealed class HttpGameVersionCatalogTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private static HttpGameVersionCatalog CreateCatalog(
        FakeHttpMessageHandler handler,
        MockFileSystem fileSystem,
        FakeClock clock,
        TimeSpan? timeToLive = null)
        => new(
            new HttpClient(handler),
            new JsonFileStore(fileSystem),
            Paths,
            clock,
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask),
            timeToLive);

    private static FakeHttpMessageHandler SamplesHandler() => new(request =>
        request.RequestUri == HttpGameVersionCatalog.StableUrl
            ? FakeHttpMessageHandler.Text(GameCatalogSamples.Stable)
            : FakeHttpMessageHandler.Text(GameCatalogSamples.Unstable));

    [Fact]
    public async Task GetAsync_FirstCall_MergesBothDocumentsAndReportsLiveData()
    {
        using var handler = SamplesHandler();
        using var catalog = CreateCatalog(handler, new MockFileSystem(), new FakeClock(Noon));

        var result = await catalog.GetAsync(cancellationToken: CancellationToken.None);

        result.Freshness.ShouldBe(GameCatalogFreshness.Live);
        result.RetrievedUtc.ShouldBe(Noon);
        result.Versions.Select(entry => entry.Version.ToString()).ShouldBe(["1.23.0-rc.1", "1.22.6", "1.21.3"]);
        handler.CountFor(HttpGameVersionCatalog.StableUrl).ShouldBe(1);
        handler.CountFor(HttpGameVersionCatalog.UnstableUrl).ShouldBe(1);
    }

    [Fact]
    public async Task GetAsync_WithinTimeToLive_ServesTheMemoryCacheWithoutTouchingTheNetwork()
    {
        using var handler = SamplesHandler();
        var clock = new FakeClock(Noon);
        using var catalog = CreateCatalog(handler, new MockFileSystem(), clock, TimeSpan.FromHours(6));

        await catalog.GetAsync(cancellationToken: CancellationToken.None);
        clock.UtcNow = Noon.AddHours(5);
        var second = await catalog.GetAsync(cancellationToken: CancellationToken.None);

        second.Freshness.ShouldBe(GameCatalogFreshness.Cached);
        second.Versions.Count.ShouldBe(3);
        handler.CountFor(HttpGameVersionCatalog.StableUrl).ShouldBe(1);
    }

    [Fact]
    public async Task GetAsync_AfterTimeToLive_GoesBackToTheNetwork()
    {
        using var handler = SamplesHandler();
        var clock = new FakeClock(Noon);
        using var catalog = CreateCatalog(handler, new MockFileSystem(), clock, TimeSpan.FromHours(6));

        await catalog.GetAsync(cancellationToken: CancellationToken.None);
        clock.UtcNow = Noon.AddHours(7);
        var second = await catalog.GetAsync(cancellationToken: CancellationToken.None);

        second.Freshness.ShouldBe(GameCatalogFreshness.Live);
        handler.CountFor(HttpGameVersionCatalog.StableUrl).ShouldBe(2);
    }

    [Fact]
    public async Task GetAsync_FreshDiskCacheWrittenByAnEarlierSession_AvoidsTheNetworkEntirely()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Noon);
        using (var first = CreateCatalog(SamplesHandler(), fileSystem, clock))
        {
            await first.GetAsync(cancellationToken: CancellationToken.None);
        }

        using var offlineHandler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("hors ligne"));
        clock.UtcNow = Noon.AddHours(1);
        using var second = CreateCatalog(offlineHandler, fileSystem, clock, TimeSpan.FromHours(6));

        var result = await second.GetAsync(cancellationToken: CancellationToken.None);

        result.Freshness.ShouldBe(GameCatalogFreshness.Cached);
        result.RetrievedUtc.ShouldBe(Noon);
        result.Versions.Count.ShouldBe(3);
        offlineHandler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsync_NetworkDownAndCacheExpired_ServesTheStaleCacheAndSaysSo()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Noon);
        using (var first = CreateCatalog(SamplesHandler(), fileSystem, clock))
        {
            await first.GetAsync(cancellationToken: CancellationToken.None);
        }

        using var offlineHandler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("hors ligne"));
        clock.UtcNow = Noon.AddDays(4);
        using var second = CreateCatalog(offlineHandler, fileSystem, clock, TimeSpan.FromHours(6));

        var result = await second.GetAsync(cancellationToken: CancellationToken.None);

        result.Freshness.ShouldBe(GameCatalogFreshness.Stale);
        result.RetrievedUtc.ShouldBe(Noon);
        result.Versions.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GetAsync_NetworkDownAndNoCacheAtAll_ThrowsTheTypedDomainException()
    {
        using var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("hors ligne"));
        using var catalog = CreateCatalog(handler, new MockFileSystem(), new FakeClock(Noon));

        var exception = await Should.ThrowAsync<GameCatalogUnavailableException>(
            () => catalog.GetAsync(cancellationToken: CancellationToken.None));

        exception.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task GetAsync_ForceRefresh_IgnoresAFreshCache()
    {
        using var handler = SamplesHandler();
        var clock = new FakeClock(Noon);
        using var catalog = CreateCatalog(handler, new MockFileSystem(), clock, TimeSpan.FromHours(6));

        await catalog.GetAsync(cancellationToken: CancellationToken.None);
        var refreshed = await catalog.GetAsync(forceRefresh: true, CancellationToken.None);

        refreshed.Freshness.ShouldBe(GameCatalogFreshness.Live);
        handler.CountFor(HttpGameVersionCatalog.StableUrl).ShouldBe(2);
    }

    [Fact]
    public async Task GetAsync_TransientServerError_IsRetriedUntilItSucceeds()
    {
        var stableAttempts = 0;
        using var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri == HttpGameVersionCatalog.UnstableUrl)
            {
                return FakeHttpMessageHandler.Text(GameCatalogSamples.Unstable);
            }

            stableAttempts++;

            return stableAttempts < 3
                ? FakeHttpMessageHandler.Status(HttpStatusCode.BadGateway)
                : FakeHttpMessageHandler.Text(GameCatalogSamples.Stable);
        });
        using var catalog = CreateCatalog(handler, new MockFileSystem(), new FakeClock(Noon));

        var result = await catalog.GetAsync(cancellationToken: CancellationToken.None);

        result.Freshness.ShouldBe(GameCatalogFreshness.Live);
        stableAttempts.ShouldBe(3);
    }

    [Fact]
    public async Task GetAsync_DefinitiveClientError_IsNotRetried()
    {
        var attempts = 0;
        using var handler = new FakeHttpMessageHandler(_ =>
        {
            attempts++;

            return FakeHttpMessageHandler.Status(HttpStatusCode.NotFound);
        });
        using var catalog = CreateCatalog(handler, new MockFileSystem(), new FakeClock(Noon));

        await Should.ThrowAsync<GameCatalogUnavailableException>(
            () => catalog.GetAsync(cancellationToken: CancellationToken.None));

        attempts.ShouldBe(1);
    }

    [Fact]
    public async Task GetAsync_MalformedPayload_FallsBackToTheStaleCache()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Noon);
        using (var first = CreateCatalog(SamplesHandler(), fileSystem, clock))
        {
            await first.GetAsync(cancellationToken: CancellationToken.None);
        }

        using var brokenHandler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Text("{ not json"));
        clock.UtcNow = Noon.AddDays(4);
        using var second = CreateCatalog(brokenHandler, fileSystem, clock, TimeSpan.FromHours(6));

        var result = await second.GetAsync(cancellationToken: CancellationToken.None);

        result.Freshness.ShouldBe(GameCatalogFreshness.Stale);
    }

    [Fact]
    public async Task GetAsync_CacheFileOnDisk_LivesUnderTheHttpCacheDirectory()
    {
        var fileSystem = new MockFileSystem();
        using var handler = SamplesHandler();
        using var catalog = CreateCatalog(handler, fileSystem, new FakeClock(Noon));

        await catalog.GetAsync(cancellationToken: CancellationToken.None);

        catalog.CacheFilePath.ShouldBe(fileSystem.Path.Combine(Paths.HttpCacheDirectory, "game-versions.json"));
        fileSystem.File.Exists(catalog.CacheFilePath).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAsync_CorruptedCacheFileAndNetworkDown_ReportsUnavailableRatherThanCrashing()
    {
        var fileSystem = new MockFileSystem();
        using var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("hors ligne"));
        using var catalog = CreateCatalog(handler, fileSystem, new FakeClock(Noon));
        fileSystem.AddFile(catalog.CacheFilePath, new MockFileData("{ not json"));

        await Should.ThrowAsync<GameCatalogUnavailableException>(
            () => catalog.GetAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_CacheWrittenByALaterSchema_IsIgnored()
    {
        var fileSystem = new MockFileSystem();
        using var handler = SamplesHandler();
        using var catalog = CreateCatalog(handler, fileSystem, new FakeClock(Noon));
        fileSystem.AddFile(catalog.CacheFilePath, new MockFileData(
            """{ "schemaVersion": 99, "retrievedUtc": "2026-08-11T12:00:00+00:00", "stableJson": "{}", "unstableJson": "{}" }"""));

        var result = await catalog.GetAsync(cancellationToken: CancellationToken.None);

        result.Freshness.ShouldBe(GameCatalogFreshness.Live);
        result.Versions.Count.ShouldBe(3);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        using var handler = SamplesHandler();
        using var client = new HttpClient(handler);
        var store = new JsonFileStore(new MockFileSystem());

        Should.Throw<ArgumentNullException>(() => new HttpGameVersionCatalog(null!, store, Paths, new FakeClock(Noon)));
        Should.Throw<ArgumentNullException>(() => new HttpGameVersionCatalog(client, null!, Paths, new FakeClock(Noon)));
        Should.Throw<ArgumentNullException>(() => new HttpGameVersionCatalog(client, store, null!, new FakeClock(Noon)));
        Should.Throw<ArgumentNullException>(() => new HttpGameVersionCatalog(client, store, Paths, null!));
    }
}