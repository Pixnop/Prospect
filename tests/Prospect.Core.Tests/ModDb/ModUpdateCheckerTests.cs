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
        FakeUpdatesServer Server,
        RecordingAppLog Log);

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

        var log = new RecordingAppLog();

        return new Harness(new ModUpdateChecker(client, repository, instances, clock, log), repository, fileSystem, server, log);
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

    /// <summary>
    /// Le défaut qui faisait passer « Vérifier les mises à jour » pour inopérant : une release plus
    /// récente, signalée par le serveur, mais qu'aucun tag ne déclare pour la version de jeu de
    /// l'instance. Elle était rendue « à jour ».
    /// </summary>
    /// <remarks>
    /// Le verdict n'est pas cosmétique, c'est TOUT ce que l'utilisateur voit d'une vérification. Sur
    /// une version de jeu récente, presque aucune release n'est encore cochée pour elle : chaque mod
    /// retombait donc sur ce chemin, et le bouton rendait invariablement « tout est à jour » alors
    /// que le serveur venait de répondre le contraire.
    /// </remarks>
    [Fact]
    public async Task CheckAsync_ANewerReleaseNotTaggedForThisVersion_IsNotPassedOffAsUpToDate()
    {
        var harness = Create("1.20.9"); // même série mineure (1.20) que la candidate, mais pas le patch exact
        SeedMod(harness, "configlib-1.0.0.zip", ModInfo("configlib", "Config lib", "1.0.0"));
        harness.Server.UpdatesJson = UpdatesJsonWithTags(["1.20.4"]);

        var report = await harness.Checker.CheckAsync(Slug, ModCompatibilityMode.ExactGameVersion, CancellationToken.None);

        var result = report.Mods.ShouldHaveSingleItem();
        result.Status.ShouldBe(ModUpdateStatus.UpdateNotDeclaredForThisVersion);
        result.HasUpdate.ShouldBeFalse("elle ne s'installe pas d'un clic");
        result.HasUndeclaredUpdate.ShouldBeTrue();

        // La release est nommée, avec ses tags réels : c'est ce que l'écran doit pouvoir dire.
        result.AvailableRelease.ShouldNotBeNull().Version.ShouldBe(ModVersion.Parse("1.12.0"));
        result.AvailableGameVersions.ShouldBe(["1.20.4"]);
        report.UndeclaredUpdateCount.ShouldBe(1);
    }

    /// <summary>
    /// Le cas voisin, qui lui reste « à jour » : le serveur a répondu, mais sa candidate n'est PAS
    /// plus récente que la copie installée. Rien de nouveau, tags ou pas.
    /// </summary>
    [Fact]
    public async Task CheckAsync_AnUntaggedCandidateThatIsNotNewer_IsGenuinelyUpToDate()
    {
        var harness = Create("1.20.9");
        SeedMod(harness, "configlib-1.12.0.zip", ModInfo("configlib", "Config lib", "1.12.0"), Provenance("configlib", "1.12.0"));
        harness.Server.UpdatesJson = UpdatesJsonWithTags(["1.20.4"]); // candidate en 1.12.0, donc pas plus récente

        var report = await harness.Checker.CheckAsync(Slug, ModCompatibilityMode.ExactGameVersion, CancellationToken.None);

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

    /// <summary>
    /// Une candidate d'une TOUTE AUTRE série n'est jamais proposée en un clic, même en mode élargi :
    /// l'élargissement s'arrête à la série mineure. Elle est en revanche rapportée pour ce qu'elle
    /// est, une release plus récente non déclarée, et non pour ce qu'elle n'est pas.
    /// </summary>
    [Fact]
    public async Task CheckAsync_CandidateFromACompletelyDifferentSeries_IsNeverOfferedEvenWidened()
    {
        var harness = Create("1.20.9");
        SeedMod(harness, "configlib-1.0.0.zip", ModInfo("configlib", "Config lib", "1.0.0"));
        harness.Server.UpdatesJson = UpdatesJsonWithTags(["1.19.0"]);

        var report = await harness.Checker.CheckAsync(Slug, ModCompatibilityMode.WidenToMinorSeries, CancellationToken.None);

        var result = report.Mods.ShouldHaveSingleItem();
        result.HasUpdate.ShouldBeFalse();
        result.Status.ShouldBe(ModUpdateStatus.UpdateNotDeclaredForThisVersion);
        result.AvailableGameVersions.ShouldBe(["1.19.0"]);
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

    // ── Topologie réelle de la session Linux ────────────────────────────────────────

    /// <summary>
    /// L'instance telle qu'elle existait sur la machine où « Vérifier les mises à jour » a été
    /// rapporté comme inopérant : 1.22.6, avec Carry On, CarryOnLib (installé sans compatibilité
    /// déclarée) et Primitive Survival.
    /// </summary>
    private static Harness RealLinuxInstance()
    {
        var harness = Create("1.22.6");

        SeedMod(
            harness,
            "carryon-2.0.0-pre.8.zip",
            ModInfo("carryon", "Carry On", "2.0.0-pre.8"),
            Provenance("carryon", "2.0.0-pre.8"));

        // Celui-ci a été posé par le dialogue de dépendance, sans compatibilité déclarée : sa
        // provenance le dit, et sa release s'arrête à 1.22.4.
        SeedMod(
            harness,
            "carryonlib-1.0.0-pre.8.zip",
            ModInfo("carryonlib", "CarryOnLib", "1.0.0-pre.8"),
            Provenance("carryonlib", "1.0.0-pre.8") with { DeclaredIncompatible = true });

        SeedMod(
            harness,
            "primitivesurvival-5.1.1.zip",
            ModInfo("primitivesurvival", "Primitive Survival", "5.1.1"),
            Provenance("primitivesurvival", "5.1.1"));

        return harness;
    }

    /// <summary>
    /// Les trois mods entrent bien dans la requête, provenance sans compatibilité déclarée comprise.
    /// Rien n'exclut ni ne fait trébucher le vérificateur sur ce cas.
    /// </summary>
    [Fact]
    public async Task CheckAsync_TheRealLinuxInstance_QueriesEveryModIncludingTheIncompatibleOne()
    {
        var harness = RealLinuxInstance();

        await harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None);

        harness.Server.RequestCount.ShouldBe(1);
        harness.Server.LastModsParam.ShouldNotBeNull()
            .Split(',')
            .ShouldBe(["carryon@2.0.0-pre.8", "carryonlib@1.0.0-pre.8", "primitivesurvival@5.1.1"], ignoreOrder: true);
    }

    /// <summary>
    /// Le verdict que l'utilisateur voyait : une mise à jour de CarryOnLib existe, elle est taguée
    /// jusqu'à 1.22.4, et l'instance est en 1.22.6. Elle ne doit plus disparaître dans « à jour ».
    /// </summary>
    [Fact]
    public async Task CheckAsync_TheRealLinuxInstance_RendersAVerdictInsteadOfSayingEverythingIsUpToDate()
    {
        var harness = RealLinuxInstance();
        harness.Server.UpdatesJson = """
        {
          "statuscode": "200",
          "updates": {
            "carryonlib": {
              "releaseid": 51902, "fileid": 112004, "mainfile": "https://moddbcdn.vintagestory.at/carryonlib_1.0.0-pre.9.zip",
              "filename": "carryonlib_1.0.0-pre.9.zip", "downloads": 12, "tags": ["1.22.0", "1.22.4"],
              "modidstr": "carryonlib", "modversion": "1.0.0-pre.9", "changelog": null, "created": "2026-08-10 09:00:00"
            }
          }
        }
        """;

        var report = await harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None);

        var carryOnLib = report.Mods.Single(mod => mod.Mod.Identity == "carryonlib");
        carryOnLib.Status.ShouldBe(ModUpdateStatus.UpdateNotDeclaredForThisVersion);
        carryOnLib.AvailableRelease.ShouldNotBeNull().Version.ShouldBe(ModVersion.Parse("1.0.0-pre.9"));
        carryOnLib.AvailableGameVersions.ShouldBe(["1.22.0", "1.22.4"]);

        // Les deux autres n'ont rien été signalés et ont une provenance : à jour, franchement.
        report.Mods.Single(mod => mod.Mod.Identity == "carryon").Status.ShouldBe(ModUpdateStatus.UpToDate);
        report.Mods.Single(mod => mod.Mod.Identity == "primitivesurvival").Status.ShouldBe(ModUpdateStatus.UpToDate);

        report.UndeclaredUpdateCount.ShouldBe(1);
        report.UpdateCount.ShouldBe(0);
    }

    // ── Journal ─────────────────────────────────────────────────────────────────────

    /// <summary>Une vérification aboutie laisse son verdict COMPTÉ dans le journal.</summary>
    [Fact]
    public async Task CheckAsync_LogsTheRequestAndTheCountedVerdict()
    {
        var harness = RealLinuxInstance();

        await harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None);

        harness.Log.Lines.ShouldContain(line => line.Level == AppLogLevel.Info && line.Message.Contains("demandée"));

        var verdict = harness.Log.Lines.Last(line => line.Level == AppLogLevel.Info).Message;
        verdict.ShouldContain("1.22.6");
        verdict.ShouldContain("3 mod(s)");
    }

    /// <summary>
    /// Un échec laisse sa RAISON, et l'exception continue son chemin. C'est ce qui rend un
    /// « ça ne marche pas » instruisible sans avoir la machine sous la main.
    /// </summary>
    [Fact]
    public async Task CheckAsync_WhenTheModDbRejectsTheRequest_LogsTheReasonAndStillThrows()
    {
        var harness = RealLinuxInstance();
        harness.Server.UpdatesJson = """{"statuscode":"400"}""";

        await Should.ThrowAsync<ModDbApiException>(
            () => harness.Checker.CheckAsync(Slug, cancellationToken: CancellationToken.None));

        harness.Log.Lines.ShouldContain(line => line.Level == AppLogLevel.Error && line.Message.Contains("échec de la vérification"));
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