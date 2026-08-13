using System.IO.Abstractions.TestingHelpers;
using System.Net;

using Prospect.Core.Common;
using Prospect.Core.Http;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Http;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

public sealed class ModDbClientTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private static ModDbClient CreateClient(
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

    private static FakeHttpMessageHandler CatalogHandler() => new(request => Route(request.RequestUri!));

    private static HttpResponseMessage Route(Uri url)
    {
        var path = url.AbsolutePath;
        var query = url.Query;

        return path switch
        {
            "/api/mods" when query.Contains("gv=", StringComparison.Ordinal) || query.Contains("gameversion=", StringComparison.Ordinal)
                => FakeHttpMessageHandler.Text(ModDbSamples.ModList),
            "/api/mods" => FakeHttpMessageHandler.Text(ModDbSamples.ModList),
            "/api/tags" => FakeHttpMessageHandler.Text(ModDbSamples.TagList),
            "/api/mod/1783" or "/api/mod/configlib" => FakeHttpMessageHandler.Text(ModDbSamples.ModDetail),
            "/api/v2/mods/install-information" => FakeHttpMessageHandler.Text(ModDbSamples.V2InstallInformationWithDependencies),
            "/api/updates" => FakeHttpMessageHandler.Text(ModDbSamples.UpdatesCheck),
            _ => FakeHttpMessageHandler.Text(ModDbSamples.NotFound),
        };
    }

    // ── Enveloppe statuscode de l'API v1 ────────────────────────────────────────────

    [Fact]
    public async Task GetModAsync_StatusCode200_ReadsTheEnvelopeAndReturnsTheMod()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var detail = await client.GetModAsync("configlib", CancellationToken.None);

        detail.ModId.ShouldBe(1783);
        detail.Name.ShouldBe("Config lib");
        detail.Releases.Count.ShouldBe(3);

        // La fiche n'a pas d'alias d'URL, donc la route par asset. Et c'est l'ASSETID (9551) qui
        // la route, pas le modid (1783) : cette assertion épinglait l'inverse et fixait ainsi le
        // défaut « c'est pas le bon lien » relevé en test réel (voir ModDbMapperTests).
        detail.AssetId.ShouldBe(9551);
        detail.PageUrl.ToString().ShouldBe("https://mods.vintagestory.at/show/mod/9551");
    }

    [Fact]
    public async Task GetModAsync_StatusCode404InsideAnHttp200_ThrowsInsteadOfTrustingTheHttpStatus()
    {
        // Le piège central de l'API v1 : fail() n'appelle jamais http_response_code().
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Text(ModDbSamples.NotFound));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var exception = await Should.ThrowAsync<ModDbApiException>(
            () => client.GetModAsync("this-mod-does-not-exist-xyz", CancellationToken.None));

        exception.StatusCode.ShouldBe("404");
        exception.IsNotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task GetModAsync_StatusCode410InsideAnHttp200_ReportsTheApplicationCode()
    {
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Text(ModDbSamples.Gone));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var exception = await Should.ThrowAsync<ModDbApiException>(
            () => client.GetModAsync("1783", CancellationToken.None));

        exception.StatusCode.ShouldBe("410");
        exception.IsNotFound.ShouldBeFalse();
    }

    [Fact]
    public async Task GetModAsync_EnvelopeWithoutStatusCode_IsTreatedAsAFailure()
    {
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Text("""{"mod":null}"""));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var exception = await Should.ThrowAsync<ModDbApiException>(
            () => client.GetModAsync("1783", CancellationToken.None));

        exception.StatusCode.ShouldBe("inconnu");
    }

    // ── DTOs défensifs sur échantillons réels ───────────────────────────────────────

    [Fact]
    public async Task GetCatalogAsync_RealSample_KeepsNullableFieldsAndCleansEmptyTagArrays()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var catalog = await client.GetCatalogAsync(cancellationToken: CancellationToken.None);

        catalog.Freshness.ShouldBe(ModDbFreshness.Live);
        catalog.Mods.Count.ShouldBe(3);

        var betterRuins = catalog.Mods[0];
        betterRuins.LogoUrl.ShouldNotBeNull();
        betterRuins.Side.ShouldBe(ModDbSide.Both);
        betterRuins.Tags.Count.ShouldBe(7);
        betterRuins.LastReleasedUtc.ShouldBe(new DateTimeOffset(2026, 7, 28, 18, 59, 32, TimeSpan.Zero));

        // logo et urlalias nuls (30-40 % du catalogue), et le [""] du GROUP_CONCAT vide.
        var configLib = catalog.Mods[1];
        configLib.LogoUrl.ShouldBeNull();
        configLib.Tags.ShouldBeEmpty();

        // modidstrs vide : les outils externes n'ont pas de modinfo.json.
        var externalTool = catalog.Mods[2];
        externalTool.ModIdStrings.ShouldBeEmpty();
        externalTool.LastReleasedUtc.ShouldBeNull();
    }

    [Fact]
    public async Task GetTagsAsync_RealSample_DropsTheEmptyEntryAndKeepsStringTagIds()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var tags = await client.GetTagsAsync(cancellationToken: CancellationToken.None);

        tags.Count.ShouldBe(3);
        tags[0].TagId.ShouldBe("467");
        tags[0].Name.ShouldBe("Absolute Cinema");
    }

    [Fact]
    public async Task GetModAsync_RealSample_ParsesReleasesNewestFirstWithTheirExactGameVersionTags()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var detail = await client.GetModAsync("1783", CancellationToken.None);

        detail.Releases[0].Version.ShouldBe(ModVersion.Parse("1.12.0"));
        detail.Releases[0].FileId.ShouldBe(88961);
        detail.Releases[0].CompatibleGameVersions.ShouldContain(GameVersion.Parse("1.22.0-rc.1"));
        detail.Releases[0].CreatedUtc.ShouldBe(new DateTimeOffset(2026, 5, 1, 12, 3, 34, TimeSpan.Zero));
        detail.Releases[^1].Version.ShouldBe(ModVersion.Parse("1.9.0"));
        detail.Releases[^1].Changelog.ShouldBeNull();
    }

    [Fact]
    public async Task GetModAsync_SendsAnIdentifiableUserAgent()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        await client.GetModAsync("1783", CancellationToken.None);

        handler.Requests[0].UserAgent.ShouldNotBeNull();
        handler.Requests[0].UserAgent!.ShouldStartWith("Prospect/");
    }

    // ── Cache mémoire, cache disque, TTL et repli hors ligne ────────────────────────

    [Fact]
    public async Task GetCatalogAsync_WithinTimeToLive_ServesTheMemoryCacheWithoutTouchingTheNetwork()
    {
        using var handler = CatalogHandler();
        var clock = new FakeClock(Noon);
        using var client = CreateClient(handler, new MockFileSystem(), clock, TimeSpan.FromHours(1));

        await client.GetCatalogAsync(cancellationToken: CancellationToken.None);
        clock.UtcNow = Noon.AddMinutes(50);
        var second = await client.GetCatalogAsync(cancellationToken: CancellationToken.None);

        second.Freshness.ShouldBe(ModDbFreshness.Cached);
        second.Mods.Count.ShouldBe(3);
        handler.Requests.Count(request => request.Url.AbsolutePath == "/api/mods").ShouldBe(1);
    }

    [Fact]
    public async Task GetCatalogAsync_AfterTimeToLive_GoesBackToTheNetwork()
    {
        using var handler = CatalogHandler();
        var clock = new FakeClock(Noon);
        using var client = CreateClient(handler, new MockFileSystem(), clock, TimeSpan.FromHours(1));

        await client.GetCatalogAsync(cancellationToken: CancellationToken.None);
        clock.UtcNow = Noon.AddHours(2);
        var second = await client.GetCatalogAsync(cancellationToken: CancellationToken.None);

        second.Freshness.ShouldBe(ModDbFreshness.Live);
        handler.Requests.Count(request => request.Url.AbsolutePath == "/api/mods").ShouldBe(2);
    }

    [Fact]
    public async Task GetCatalogAsync_FreshDiskCacheFromAnEarlierSession_AvoidsTheNetworkEntirely()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Noon);
        using (var first = CreateClient(CatalogHandler(), fileSystem, clock))
        {
            await first.GetCatalogAsync(cancellationToken: CancellationToken.None);
        }

        clock.UtcNow = Noon.AddMinutes(30);
        using var offline = new FakeHttpMessageHandler(_ => throw new HttpRequestException("réseau coupé"));
        using var second = CreateClient(offline, fileSystem, clock);

        var catalog = await second.GetCatalogAsync(cancellationToken: CancellationToken.None);

        catalog.Freshness.ShouldBe(ModDbFreshness.Cached);
        catalog.Mods.Count.ShouldBe(3);
        offline.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetCatalogAsync_ExpiredCacheAndUnreachableApi_ServesTheStaleCacheAndSaysSo()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Noon);
        using (var first = CreateClient(CatalogHandler(), fileSystem, clock, TimeSpan.FromHours(1)))
        {
            await first.GetCatalogAsync(cancellationToken: CancellationToken.None);
        }

        clock.UtcNow = Noon.AddDays(3);
        using var offline = new FakeHttpMessageHandler(_ => throw new HttpRequestException("réseau coupé"));
        using var second = CreateClient(offline, fileSystem, clock, TimeSpan.FromHours(1));

        var catalog = await second.GetCatalogAsync(cancellationToken: CancellationToken.None);

        catalog.Freshness.ShouldBe(ModDbFreshness.Stale);
        catalog.Mods.Count.ShouldBe(3);
        catalog.RetrievedUtc.ShouldBe(Noon);
    }

    [Fact]
    public async Task GetCatalogAsync_NoCacheAndUnreachableApi_ReportsTheOutage()
    {
        using var offline = new FakeHttpMessageHandler(_ => throw new HttpRequestException("réseau coupé"));
        using var client = CreateClient(offline, new MockFileSystem(), new FakeClock(Noon));

        await Should.ThrowAsync<ModDbUnavailableException>(
            () => client.GetCatalogAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task GetCatalogAsync_ForceRefresh_IgnoresAFreshCache()
    {
        using var handler = CatalogHandler();
        var clock = new FakeClock(Noon);
        using var client = CreateClient(handler, new MockFileSystem(), clock, TimeSpan.FromHours(1));

        await client.GetCatalogAsync(cancellationToken: CancellationToken.None);
        var second = await client.GetCatalogAsync(forceRefresh: true, CancellationToken.None);

        second.Freshness.ShouldBe(ModDbFreshness.Live);
        handler.Requests.Count(request => request.Url.AbsolutePath == "/api/mods").ShouldBe(2);
    }

    [Fact]
    public async Task GetCatalogAsync_CorruptedDiskCache_FallsBackToTheNetworkInsteadOfFailing()
    {
        var fileSystem = new MockFileSystem();
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, fileSystem, new FakeClock(Noon));
        fileSystem.AddFile(client.CacheFilePath, new MockFileData("{ pas du json"));

        var catalog = await client.GetCatalogAsync(cancellationToken: CancellationToken.None);

        catalog.Freshness.ShouldBe(ModDbFreshness.Live);
        catalog.Mods.Count.ShouldBe(3);
    }

    // ── Index de compatibilité ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetCompatibilityIndexAsync_ExactVersion_UsesTheGvParameterAndCachesTheAnswer()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var index = await client.GetCompatibilityIndexAsync(GameVersion.Parse("1.21.3"), cancellationToken: CancellationToken.None);
        var second = await client.GetCompatibilityIndexAsync(GameVersion.Parse("1.21.3"), cancellationToken: CancellationToken.None);

        index.IsApproximate.ShouldBeFalse();
        index.Contains(792).ShouldBeTrue();
        second.Freshness.ShouldBe(ModDbFreshness.Cached);
        handler.Requests.Count(request => request.Url.Query.Contains("gv=1.21.3", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task GetCompatibilityIndexAsync_WidenedToTheMinorSeries_UsesTheTwoComponentParameterAndFlagsTheApproximation()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var index = await client.GetCompatibilityIndexAsync(GameVersion.Parse("1.21.3"), widenToMinorSeries: true, CancellationToken.None);

        index.IsApproximate.ShouldBeTrue();
        handler.Requests.ShouldContain(request => request.Url.Query.Contains("gameversion=1.21", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCompatibilityIndexAsync_UnreachableApi_DegradesToAnEmptyIndexRatherThanFailing()
    {
        using var offline = new FakeHttpMessageHandler(_ => throw new HttpRequestException("réseau coupé"));
        using var client = CreateClient(offline, new MockFileSystem(), new FakeClock(Noon));

        var index = await client.GetCompatibilityIndexAsync(GameVersion.Parse("1.21.3"), cancellationToken: CancellationToken.None);

        index.ModIds.ShouldBeEmpty();
        index.Freshness.ShouldBe(ModDbFreshness.Stale);
    }

    [Fact]
    public async Task GetCompatibilityIndexAsync_ApiErrorStatusCodeOnV1_DegradesToAnEmptyIndexRatherThanThrowing()
    {
        // Écart de contrat aligné sur GetInstallInformationAsync_HttpErrorOnV2 : un statuscode v1
        // différent de "200" (piège habituel de cette API, voir GetModAsync_StatusCode404InsideAnHttp200)
        // dégradait jusqu'ici en ModDbApiException non rattrapée au lieu de suivre la même règle que
        // le reste de cette méthode — un badge de compatibilité manquant plutôt qu'un écran de
        // recherche qui plante.
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Text(ModDbSamples.NotFound));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var index = await client.GetCompatibilityIndexAsync(GameVersion.Parse("1.21.3"), cancellationToken: CancellationToken.None);

        index.ModIds.ShouldBeEmpty();
        index.Freshness.ShouldBe(ModDbFreshness.Stale);
    }

    // ── install-information (v2, vrais codes HTTP) ───────────────────────────────────

    [Fact]
    public async Task GetInstallInformationAsync_RealSample_ReadsRecommendedUpgradeAndResolvedDependencies()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var information = await client.GetInstallInformationAsync(
            ["configlib"],
            GameVersion.Parse("1.22.0"),
            CancellationToken.None);

        information["configlib"].RecommendedVersion.ShouldBe(ModVersion.Parse("1.12.0"));
        information["configlib"].ResolvedDependencies.ShouldContain("vsimgui");
        information["configlib"].Warnings.Count.ShouldBe(1);
        information.ShouldContainKey("vsimgui");
    }

    [Fact]
    public async Task GetInstallInformationAsync_SendsResolveDepsAndTheTargetGameVersion()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        await client.GetInstallInformationAsync(["configlib", "vsimgui"], GameVersion.Parse("1.22.0"), CancellationToken.None);

        var query = handler.Requests.Single(request => request.Url.AbsolutePath == "/api/v2/mods/install-information").Url.Query;
        query.ShouldContain("resolve-deps=true");
        query.ShouldContain("gv=1.22.0");
        query.ShouldContain("configlib%2Cvsimgui");
    }

    [Fact]
    public async Task GetInstallInformationAsync_HttpErrorOnV2_DegradesToAnEmptyMapBecauseTheLocalCheckIsTheAuthority()
    {
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Status(HttpStatusCode.BadRequest));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var information = await client.GetInstallInformationAsync(["configlib"], GameVersion.Parse("1.22.0"), CancellationToken.None);

        information.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetInstallInformationAsync_NoIdentifiers_DoesNotCallTheApiAtAll()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var information = await client.GetInstallInformationAsync([], GameVersion.Parse("1.22.0"), CancellationToken.None);

        information.ShouldBeEmpty();
        handler.Requests.ShouldBeEmpty();
    }

    // ── Vérification de mise à jour en masse (/api/updates) ──────────────────────────

    [Fact]
    public async Task GetUpdatesAsync_RealSample_ReturnsTheOutOfDateReleaseKeyedByTheSentModIdString()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var updates = await client.GetUpdatesAsync(
            new Dictionary<string, ModVersion> { ["configlib"] = ModVersion.Parse("1.0.0") },
            CancellationToken.None);

        updates.ShouldContainKey("configlib");
        var release = updates["configlib"];
        release.Version.ShouldBe(ModVersion.Parse("1.12.0"));
        release.ReleaseId.ShouldBe(39980);
        release.FileId.ShouldBe(88961);
        release.CompatibleGameVersionTags.ShouldContain("1.22.1");
        release.Changelog.ShouldBeNull();
    }

    [Fact]
    public async Task GetUpdatesAsync_JoinsModIdAtVersionPairsWithCommasAndEscapesTheWhole()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        await client.GetUpdatesAsync(
            new Dictionary<string, ModVersion>
            {
                ["configlib"] = ModVersion.Parse("1.0.0"),
                ["jsonpatcheslib"] = ModVersion.Parse("1.5.6"),
            },
            CancellationToken.None);

        var query = handler.Requests.Single(request => request.Url.AbsolutePath == "/api/updates").Url.Query;
        query.ShouldContain("configlib%401.0.0%2Cjsonpatcheslib%401.5.6");
    }

    [Fact]
    public async Task GetUpdatesAsync_NoInstalledMods_DoesNotCallTheApiAtAll()
    {
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var updates = await client.GetUpdatesAsync(new Dictionary<string, ModVersion>(), CancellationToken.None);

        updates.ShouldBeEmpty();
        handler.Requests.ShouldBeEmpty();
    }

    /// <summary>
    /// Le défaut de terrain : un lot dont RIEN n'est en retard répond
    /// <c>{"statuscode":"200","updates":[]}</c>, parce que PHP encode une map associative vide en
    /// tableau. La désérialisation attendait un objet, levait, et le client rendait « Le ModDB est
    /// injoignable » entre deux téléchargements ModDB réussis.
    /// </summary>
    [Fact]
    public async Task GetUpdatesAsync_NothingIsBehind_ReadsThePhpEmptyArrayAsAnEmptyMap()
    {
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Text(ModDbSamples.UpdatesNoneBehind));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var updates = await client.GetUpdatesAsync(
            new Dictionary<string, ModVersion>
            {
                ["carryon"] = ModVersion.Parse("2.0.0-pre.8"),
                ["carryonlib"] = ModVersion.Parse("1.0.0-pre.8"),
                ["primitivesurvival"] = ModVersion.Parse("5.1.1"),
            },
            CancellationToken.None);

        updates.ShouldBeEmpty();
    }

    /// <summary>Un tableau NON vide n'a pas de clés : rien n'en ferait une map, il reste refusé.</summary>
    [Fact]
    public async Task GetUpdatesAsync_ANonEmptyArrayWhereAMapBelongs_IsStillRefused()
    {
        using var handler = new FakeHttpMessageHandler(
            _ => FakeHttpMessageHandler.Text("""{"statuscode":"200","updates":[{"releaseid":1}]}"""));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        await Should.ThrowAsync<ModDbUnavailableException>(() => client.GetUpdatesAsync(
            new Dictionary<string, ModVersion> { ["configlib"] = ModVersion.Parse("1.0.0") },
            CancellationToken.None));
    }

    [Fact]
    public async Task GetInstallInformationAsync_EmptyDataAndResolvedArrays_AreReadAsEmptyMaps()
    {
        // Même piège PHP sur v2 : `data` et `resolved` vides sortent en tableaux. Sans le
        // convertisseur, la JsonException était avalée par le rattrapage de GetInstallInformationAsync
        // et le croisement resolve-deps disparaissait en silence sur tout mod sans dépendance.
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Text(ModDbSamples.V2InstallInformationAllEmpty));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var information = await client.GetInstallInformationAsync(["configlib"], GameVersion.Parse("1.22.0"), CancellationToken.None);

        information.ShouldBeEmpty();
        handler.Requests.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GetUpdatesAsync_RejectedByTheServer_ThrowsRatherThanSilentlyReportingNoUpdates()
    {
        // /api/updates est un des rares endpoints v1 où un vrai code HTTP 400 sort (docs/research/moddb-api.md),
        // sur une entrée sans @ : notre client ne peut jamais en construire lui-même, mais si le
        // serveur rejette quand même la requête, ça ne doit surtout pas se lire comme « aucun mod en
        // retard ». EnsureSuccessStatusCode() sur GetStringAsync lève une HttpRequestException, que
        // GetUpdatesAsync convertit en ModDbUnavailableException plutôt que de la laisser fuiter :
        // c'est le contrat documenté sur IModDbClient.GetUpdatesAsync.
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Status(HttpStatusCode.BadRequest));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        await Should.ThrowAsync<ModDbUnavailableException>(() => client.GetUpdatesAsync(
            new Dictionary<string, ModVersion> { ["configlib"] = ModVersion.Parse("1.0.0") },
            CancellationToken.None));
    }

    // ── HEAD de taille, seul garde-fou disponible faute de checksum ──────────────────

    [Fact]
    public async Task GetFileSizeAsync_AnnouncedContentLength_IsReturned()
    {
        var url = new Uri("https://moddbcdn.vintagestory.at/configlib_1.12.0_abc.zip");
        using var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[198885]) };

            return response;
        });
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var size = await client.GetFileSizeAsync(url, CancellationToken.None);

        size.ShouldBe(198885);
        handler.Requests.Single().Method.ShouldBe(HttpMethod.Head);
    }

    [Fact]
    public async Task GetFileSizeAsync_UnreachableCdn_ReturnsNullRatherThanFailing()
    {
        using var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("cdn coupé"));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var size = await client.GetFileSizeAsync(new Uri("https://moddbcdn.vintagestory.at/x.zip"), CancellationToken.None);

        size.ShouldBeNull();
    }

    // ── Traduction uniforme des pannes de transport (audit PR #22) ───────────────────
    //
    // HttpClient lève la même TaskCanceledException pour un timeout interne (HttpClient.Timeout)
    // et pour une annulation demandée par l'appelant : IsNetworkFailure ne les distinguait pas et
    // GetModAsync n'avait même aucun bloc catch, laissant fuir HttpRequestException et
    // TaskCanceledException brutes en violation du contrat documenté sur IModDbClient. Un test par
    // méthode publique ci-dessous prouve la traduction en ModDbUnavailableException (ou le repli
    // documenté de la méthode) sur un timeout non demandé par l'appelant, et deux tests séparés
    // prouvent qu'une authentique annulation utilisateur, elle, n'est jamais traduite.

    [Fact]
    public async Task GetCatalogAsync_NoCacheAndTransportTimeout_ThrowsModDbUnavailableException()
    {
        using var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("délai dépassé"));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        await Should.ThrowAsync<ModDbUnavailableException>(
            () => client.GetCatalogAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task GetTagsAsync_NoCacheAndTransportTimeout_ThrowsModDbUnavailableException()
    {
        using var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("délai dépassé"));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        await Should.ThrowAsync<ModDbUnavailableException>(
            () => client.GetTagsAsync(cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task GetModAsync_TransportTimeout_ThrowsModDbUnavailableExceptionInsteadOfLeakingIt()
    {
        // Le cœur du correctif : avant lui, GetModAsync n'avait aucun bloc catch autour de
        // ReadV1Async, donc rien ne traduisait cette TaskCanceledException.
        using var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("délai dépassé"));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var exception = await Should.ThrowAsync<ModDbUnavailableException>(
            () => client.GetModAsync("configlib", CancellationToken.None));

        exception.InnerException.ShouldBeOfType<TaskCanceledException>();
    }

    [Fact]
    public async Task GetCompatibilityIndexAsync_TransportTimeout_DegradesToAnEmptyIndexRatherThanLeakingTheException()
    {
        using var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("délai dépassé"));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var index = await client.GetCompatibilityIndexAsync(GameVersion.Parse("1.21.3"), cancellationToken: CancellationToken.None);

        index.ModIds.ShouldBeEmpty();
        index.Freshness.ShouldBe(ModDbFreshness.Stale);
    }

    [Fact]
    public async Task GetInstallInformationAsync_TransportTimeout_DegradesToAnEmptyMapRatherThanLeakingTheException()
    {
        using var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("délai dépassé"));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var information = await client.GetInstallInformationAsync(["configlib"], GameVersion.Parse("1.22.0"), CancellationToken.None);

        information.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetUpdatesAsync_TransportTimeout_ThrowsModDbUnavailableException()
    {
        using var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("délai dépassé"));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        await Should.ThrowAsync<ModDbUnavailableException>(() => client.GetUpdatesAsync(
            new Dictionary<string, ModVersion> { ["configlib"] = ModVersion.Parse("1.0.0") },
            CancellationToken.None));
    }

    [Fact]
    public async Task GetFileSizeAsync_TransportTimeout_ReturnsNullRatherThanLeakingTheException()
    {
        using var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("délai dépassé"));
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));

        var size = await client.GetFileSizeAsync(new Uri("https://moddbcdn.vintagestory.at/x.zip"), CancellationToken.None);

        size.ShouldBeNull();
    }

    [Fact]
    public async Task GetModAsync_CallerCancelled_PropagatesOperationCanceledExceptionWithoutTranslating()
    {
        // Une authentique annulation demandée par l'appelant est une décision, pas une panne réseau :
        // à la différence du timeout testé plus haut, elle doit ressortir intacte plutôt que de se
        // faire traduire en ModDbUnavailableException.
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => client.GetModAsync("configlib", cts.Token));
    }

    [Fact]
    public async Task GetFileSizeAsync_CallerCancelled_PropagatesOperationCanceledExceptionRatherThanReturningNull()
    {
        // Complète le test précédent sur une méthode qui se replie normalement sur une valeur par
        // défaut (null) au lieu de lever : une annulation authentique ne doit pas se lire pareil,
        // ce serait la masquer complètement plutôt que la laisser remonter à l'appelant.
        using var handler = CatalogHandler();
        using var client = CreateClient(handler, new MockFileSystem(), new FakeClock(Noon));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => client.GetFileSizeAsync(new Uri("https://moddbcdn.vintagestory.at/x.zip"), cts.Token));
    }
}