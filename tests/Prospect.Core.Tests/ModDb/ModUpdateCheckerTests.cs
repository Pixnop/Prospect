using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Text.Json;

using Prospect.Core.Common;
using Prospect.Core.Http;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Http;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

public sealed class ModUpdateCheckerTests
{
    private const string Slug = "homestead-121";

    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        ModUpdateChecker Checker,
        IInstalledModRepository Repository,
        MockFileSystem FileSystem,
        FakeUpdatesServer Server);

    private static Harness Create(string gameVersion = "1.21.3")
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Noon);
        var store = new JsonFileStore(fileSystem);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, store, new InstanceMetadataMigrationPipeline([]));
        var archiveReader = new ModArchiveReader(fileSystem);
        var repository = new FileSystemInstalledModRepository(
            fileSystem,
            instances,
            archiveReader,
            new DisabledSuffixModStateConvention(),
            store);

        var server = new FakeUpdatesServer();
        var handler = new FakeHttpMessageHandler(server.Respond);
        var client = new ModDbClient(
            new HttpClient(handler),
            store,
            Paths,
            clock,
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask));

        SeedInstance(fileSystem, gameVersion);

        return new Harness(new ModUpdateChecker(client, repository, instances, clock), repository, fileSystem, server);
    }

    private static void SeedInstance(MockFileSystem fileSystem, string gameVersion)
    {
        var metadata = new InstanceMetadata
        {
            SchemaVersion = InstanceMetadata.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            Name = "Homestead 1.21",
            GameVersion = GameVersion.Parse(gameVersion),
            CreatedUtc = Noon,
        };

        fileSystem.AddFile(
            fileSystem.Path.Combine(Paths.InstancesDirectory, Slug, "instance.json"),
            new MockFileData(JsonSerializer.Serialize(metadata, InstanceJsonContext.Default.InstanceMetadata)));
        fileSystem.AddDirectory(fileSystem.Path.Combine(Paths.InstancesDirectory, Slug, "data", "Mods"));
    }

    private static void SeedMod(Harness harness, string fileName, string modInfoJson, ModProvenance? provenance = null)
    {
        var modsDirectory = harness.Repository.GetModsDirectory(Slug);
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(modsDirectory, fileName),
            new MockFileData(ModInfoSamples.BuildArchive(modInfoJson)));

        if (provenance is not null)
        {
            harness.Repository.SaveProvenanceAsync(Slug, provenance, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    private static ModProvenance Provenance(string modIdString, string version = "1.0.0") => new()
    {
        FileName = $"{modIdString}-{version}.zip",
        ModId = 1783,
        ModIdString = modIdString,
        ReleaseId = 1,
        FileId = 1,
        Version = ModVersion.Parse(version),
        InstalledUtc = Noon,
    };

    private static string ModInfo(string modId, string name, string version) => $$"""
    { "type": "code", "modid": "{{modId}}", "name": "{{name}}", "version": "{{version}}", "authors": ["Quelqu'un"] }
    """;

    // ── Échantillon réel, forme du résultat ─────────────────────────────────────────

    [Fact]
    public async Task CheckAsync_RealSample_ReportsTheOutOfDateModWithItsTargetReleaseAndSize()
    {
        var harness = Create("1.22.0");
        SeedMod(harness, "configlib-1.0.0.zip", ModInfo("configlib", "Config lib", "1.0.0"), Provenance("configlib"));
        harness.Server.UpdatesJson = ModDbSamples.UpdatesCheck;
        harness.Server.AnnouncedFileSize = 198885;

        var report = await harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None);

        var result = report.Mods.ShouldHaveSingleItem();
        result.Status.ShouldBe(ModUpdateStatus.UpdateAvailable);
        result.AvailableRelease.ShouldNotBeNull();
        result.AvailableRelease!.Version.ShouldBe(ModVersion.Parse("1.12.0"));
        result.AnnouncedSizeBytes.ShouldBe(198885);
        result.IsApproximateMatch.ShouldBeFalse();
        report.UpdateCount.ShouldBe(1);
        report.HasUpdates.ShouldBeTrue();
        report.CheckedUtc.ShouldBe(Noon);
    }

    [Fact]
    public async Task CheckAsync_CallsTheUpdatesEndpointExactlyOnceRegardlessOfModCount()
    {
        var harness = Create();
        SeedMod(harness, "configlib-1.0.0.zip", ModInfo("configlib", "Config lib", "1.0.0"));
        SeedMod(harness, "jsonpatcheslib-1.0.0.zip", ModInfo("jsonpatcheslib", "JSON Patches lib", "1.0.0"));

        await harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None);

        harness.Server.RequestCount.ShouldBe(1);
    }

    // ── Piège absence-vs-à-jour (docs/research/moddb-api.md) ────────────────────────

    [Fact]
    public async Task CheckAsync_AbsentFromTheResponseWithModDbProvenance_IsUpToDate()
    {
        var harness = Create();
        SeedMod(harness, "configlib-1.11.1.zip", ModInfo("configlib", "Config lib", "1.11.1"), Provenance("configlib", "1.11.1"));
        harness.Server.UpdatesJson = """{"statuscode":"200","updates":{}}""";

        var report = await harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None);

        report.Mods.ShouldHaveSingleItem().Status.ShouldBe(ModUpdateStatus.UpToDate);
    }

    [Fact]
    public async Task CheckAsync_AbsentFromTheResponseWithoutProvenance_IsUnknownRatherThanAssumedUpToDate()
    {
        // Le piège exact de la recherche : sans provenance, rien ne confirme que ce modidstr est
        // même connu du ModDB. L'absence seule ne suffit jamais à conclure « à jour ».
        var harness = Create();
        SeedMod(harness, "configlib-1.11.1.zip", ModInfo("configlib", "Config lib", "1.11.1"));
        harness.Server.UpdatesJson = """{"statuscode":"200","updates":{}}""";

        var report = await harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None);

        report.Mods.ShouldHaveSingleItem().Status.ShouldBe(ModUpdateStatus.UnknownToModDb);
    }

    // ── Filtre de compatibilité par tags, strict puis élargi ────────────────────────

    [Fact]
    public async Task CheckAsync_CandidateTaggedForTheExactGameVersion_IsAnExactUpdate()
    {
        var harness = Create("1.22.1");
        SeedMod(harness, "configlib-1.0.0.zip", ModInfo("configlib", "Config lib", "1.0.0"));
        harness.Server.UpdatesJson = ModDbSamples.UpdatesCheck; // tags incluent 1.22.1

        var report = await harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None);

        var result = report.Mods.ShouldHaveSingleItem();
        result.Status.ShouldBe(ModUpdateStatus.UpdateAvailable);
        result.IsApproximateMatch.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckAsync_CandidateNotTaggedForTheExactVersion_StrictMode_IsNotOfferedAsAnUpdate()
    {
        var harness = Create("1.20.9"); // même série mineure (1.20) que la candidate, mais pas le patch exact
        SeedMod(harness, "configlib-1.0.0.zip", ModInfo("configlib", "Config lib", "1.0.0"));
        harness.Server.UpdatesJson = UpdatesJsonWithTags(["1.20.4"]);

        var report = await harness.Checker.CheckAsync(Slug, ModCompatibilityMode.ExactGameVersion, CancellationToken.None);

        // Aucune release confirmée compatible : pas d'affirmation « à jour » à tort, mais rien
        // d'actionnable non plus tant que le mode strict est en vigueur.
        report.Mods.ShouldHaveSingleItem().Status.ShouldBe(ModUpdateStatus.UpToDate);
    }

    [Fact]
    public async Task CheckAsync_CandidateNotTaggedForTheExactVersion_WidenedMode_IsOfferedAndFlaggedApproximate()
    {
        var harness = Create("1.20.9");
        SeedMod(harness, "configlib-1.0.0.zip", ModInfo("configlib", "Config lib", "1.0.0"));
        harness.Server.UpdatesJson = UpdatesJsonWithTags(["1.20.4"]);

        var report = await harness.Checker.CheckAsync(Slug, ModCompatibilityMode.WidenToMinorSeries, CancellationToken.None);

        var result = report.Mods.ShouldHaveSingleItem();
        result.Status.ShouldBe(ModUpdateStatus.UpdateAvailable);
        result.IsApproximateMatch.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckAsync_CandidateFromACompletelyDifferentSeries_IsNeverOfferedEvenWidened()
    {
        var harness = Create("1.20.9");
        SeedMod(harness, "configlib-1.0.0.zip", ModInfo("configlib", "Config lib", "1.0.0"));
        harness.Server.UpdatesJson = UpdatesJsonWithTags(["1.19.0"]);

        var report = await harness.Checker.CheckAsync(Slug, ModCompatibilityMode.WidenToMinorSeries, CancellationToken.None);

        report.Mods.ShouldHaveSingleItem().Status.ShouldBe(ModUpdateStatus.UpToDate);
    }

    // ── Mods désactivés inclus, non identifiés exclus ────────────────────────────────

    [Fact]
    public async Task CheckAsync_DisabledMod_IsStillCheckedAgainstTheModDb()
    {
        var harness = Create("1.22.1");
        SeedMod(harness, "configlib-1.0.0.zip.disabled", ModInfo("configlib", "Config lib", "1.0.0"));
        harness.Server.UpdatesJson = ModDbSamples.UpdatesCheck;

        var report = await harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None);

        var result = report.Mods.ShouldHaveSingleItem();
        result.Mod.IsEnabled.ShouldBeFalse();
        result.Status.ShouldBe(ModUpdateStatus.UpdateAvailable);
    }

    [Fact]
    public async Task CheckAsync_UnreadableArchive_IsUnidentifiableAndExcludedFromTheRequest()
    {
        var harness = Create();
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(harness.Repository.GetModsDirectory(Slug), "broken.zip"),
            new MockFileData("pas une archive"));

        var report = await harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None);

        report.Mods.ShouldHaveSingleItem().Status.ShouldBe(ModUpdateStatus.Unidentifiable);
        harness.Server.LastModsParam.ShouldBeNull();
    }

    [Fact]
    public async Task CheckAsync_MixOfIdentifiedAndUnidentified_OnlyTheIdentifiedOneReachesTheQuery()
    {
        var harness = Create();
        SeedMod(harness, "configlib-1.0.0.zip", ModInfo("configlib", "Config lib", "1.0.0"));
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(harness.Repository.GetModsDirectory(Slug), "broken.zip"),
            new MockFileData("pas une archive"));

        var report = await harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None);

        report.Mods.Count.ShouldBe(2);
        report.Mods.Single(mod => mod.Mod.FileName == "broken.zip").Status.ShouldBe(ModUpdateStatus.Unidentifiable);
        harness.Server.LastModsParam.ShouldBe("configlib@1.0.0");
    }

    // ── Duplication de modidstr entre un actif et un désactivé ──────────────────────

    [Fact]
    public async Task CheckAsync_SameModIdInstalledTwiceAtDifferentVersions_SendsThePessimisticVersionButEvaluatesEachFileOnItsOwn()
    {
        var harness = Create("1.22.1");
        SeedMod(harness, "configlib-1.0.0.zip", ModInfo("configlib", "Config lib", "1.0.0"));
        SeedMod(harness, "configlib-1.12.0.zip.disabled", ModInfo("configlib", "Config lib", "1.12.0"));
        harness.Server.UpdatesJson = ModDbSamples.UpdatesCheck; // release 1.12.0

        var report = await harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None);

        harness.Server.LastModsParam.ShouldBe("configlib@1.0.0");
        report.Mods.Single(mod => mod.Mod.FileName == "configlib-1.0.0.zip").Status.ShouldBe(ModUpdateStatus.UpdateAvailable);
        report.Mods.Single(mod => mod.Mod.FileName == "configlib-1.12.0.zip").Status.ShouldBe(ModUpdateStatus.UpToDate);
    }

    private static string UpdatesJsonWithTags(IReadOnlyList<string> tags)
    {
        var tagsJson = string.Join(',', tags.Select(tag => $"\"{tag}\""));

        return $$"""
        {
          "statuscode": "200",
          "updates": {
            "configlib": {
              "releaseid": 1, "fileid": 1, "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.12.0.zip",
              "filename": "configlib_1.12.0.zip", "downloads": 1, "tags": [{{tagsJson}}], "modidstr": "configlib",
              "modversion": "1.12.0", "changelog": null, "created": "2026-05-01 12:03:34"
            }
          }
        }
        """;
    }

    /// <summary>Serveur ModDB factice ne servant que <c>/api/updates</c> et les <c>HEAD</c> du CDN.</summary>
    private sealed class FakeUpdatesServer
    {
        public string UpdatesJson { get; set; } = """{"statuscode":"200","updates":{}}""";

        public long AnnouncedFileSize { get; set; } = 1024;

        public int RequestCount { get; private set; }

        public string? LastModsParam { get; private set; }

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var url = request.RequestUri!;

            if (url.Host == "moddbcdn.vintagestory.at")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[AnnouncedFileSize]) };
                response.Content.Headers.ContentLength = AnnouncedFileSize;

                return response;
            }

            if (url.AbsolutePath == "/api/updates")
            {
                RequestCount++;
                LastModsParam = System.Web.HttpUtility.ParseQueryString(url.Query)["mods"];

                return FakeHttpMessageHandler.Text(UpdatesJson);
            }

            return FakeHttpMessageHandler.Text(ModDbSamples.NotFound);
        }
    }
}
