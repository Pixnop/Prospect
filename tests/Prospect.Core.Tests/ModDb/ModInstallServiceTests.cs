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

public sealed class ModInstallServiceTests
{
    private const string Slug = "homestead-121";

    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        ModInstallService Service,
        IInstalledModRepository Repository,
        MockFileSystem FileSystem,
        FakeModDbServer Server);

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

        var server = new FakeModDbServer();
        var handler = new FakeHttpMessageHandler(server.Respond);
        var client = new ModDbClient(
            new HttpClient(handler),
            store,
            Paths,
            clock,
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask));
        var downloads = new DownloadManager(new HttpClient(handler), fileSystem, Paths, clock);

        SeedInstance(fileSystem, gameVersion);

        return new Harness(
            new ModInstallService(client, downloads, repository, instances, archiveReader, fileSystem, clock),
            repository,
            fileSystem,
            server);
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

    // ── Choix de release ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_PicksTheNewestReleaseTaggedForTheInstanceGameVersion()
    {
        var harness = Create("1.21.3");

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        plan.Primary.Version.ShouldBe(ModVersion.Parse("1.11.1"));
        plan.Primary.IsApproximateMatch.ShouldBeFalse();
        plan.GameVersion.ShouldBe(GameVersion.Parse("1.21.3"));
    }

    [Fact]
    public async Task PrepareAsync_NoReleaseForThatGameVersion_IsReportedRatherThanInstallingSomethingElse()
    {
        var harness = Create("1.19.8");

        var exception = await Should.ThrowAsync<ModReleaseNotFoundException>(
            () => harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None));

        exception.GameVersion.ShouldBe(GameVersion.Parse("1.19.8"));
    }

    [Fact]
    public async Task PrepareAsync_WidenedToTheMinorSeries_AcceptsAnApproximateMatchAndSaysSo()
    {
        var harness = Create("1.21.9");

        var plan = await harness.Service.PrepareAsync(
            Slug,
            1783,
            ModCompatibilityMode.WidenToMinorSeries,
            cancellationToken: CancellationToken.None);

        plan.Primary.IsApproximateMatch.ShouldBeTrue();
        plan.NeedsConfirmation.ShouldBeTrue();
    }

    [Fact]
    public async Task PrepareAsync_UnknownMod_SurfacesTheApplicationLevelNotFound()
    {
        var harness = Create();

        var exception = await Should.ThrowAsync<ModDbApiException>(
            () => harness.Service.PrepareAsync(Slug, 999999, cancellationToken: CancellationToken.None));

        exception.IsNotFound.ShouldBeTrue();
    }

    // ── Nommage et provenance ───────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_WritesTheArchiveUnderTheModIdAndVersionConvention()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var expected = harness.FileSystem.Path.Combine(harness.Repository.GetModsDirectory(Slug), "configlib-1.11.1.zip");
        harness.FileSystem.File.Exists(expected).ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyAsync_NeverReusesThePublishedFileName()
    {
        // ExtraInfo-v2.2.1.zip côté ModDB pour une version qui vaut 2.2.1 : le nom publié est libre
        // et vient d'une source distante, il n'a rien à faire tel quel sur notre disque.
        var harness = Create("1.22.0");
        var plan = await harness.Service.PrepareAsync(Slug, 4400, cancellationToken: CancellationToken.None);

        await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var mods = harness.FileSystem.Directory.GetFiles(harness.Repository.GetModsDirectory(Slug));
        mods.ShouldHaveSingleItem().ShouldEndWith("extrainfo-2.2.1.zip");
    }

    [Fact]
    public async Task ApplyAsync_RecordsTheProvenanceTheZipCannotKnow()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var provenance = (await harness.Repository.LoadProvenanceAsync(Slug, CancellationToken.None))["configlib-1.11.1.zip"];
        provenance.ModId.ShouldBe(1783);
        provenance.ModIdString.ShouldBe("configlib");
        provenance.ReleaseId.ShouldBe(38314);
        provenance.FileId.ShouldBe(84120);
        provenance.Version.ShouldBe(ModVersion.Parse("1.11.1"));
        provenance.InstalledUtc.ShouldBe(Noon);
        provenance.ApproximateMatch.ShouldBeFalse();
    }

    [Fact]
    public async Task ApplyAsync_ApproximateMatch_IsRememberedInTheProvenance()
    {
        var harness = Create("1.21.9");
        var plan = await harness.Service.PrepareAsync(Slug, 1783, ModCompatibilityMode.WidenToMinorSeries, cancellationToken: CancellationToken.None);

        await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        (await harness.Repository.LoadProvenanceAsync(Slug, CancellationToken.None))
            .Values.ShouldHaveSingleItem().ApproximateMatch.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyAsync_ReturnsTheInstalledModWithItsParsedMetadata()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var installed = outcome.Installed.ShouldHaveSingleItem();
        installed.Identity.ShouldBe("configlib");
        installed.IsEnabled.ShouldBeTrue();
        installed.Provenance.ShouldNotBeNull();
    }

    // ── Plan de dépendances ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_MissingDependencyDeclaredInTheDownloadedModInfo_IsProposed()
    {
        var harness = Create();

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        plan.MissingDependencies.ShouldHaveSingleItem().ModIdString.ShouldBe("vsimgui");
        plan.Issues.ShouldContain(issue => issue.ModIdString == "vsimgui" && issue.Status == ModDependencyStatus.Missing);
        plan.NeedsConfirmation.ShouldBeTrue();
    }

    [Fact]
    public async Task PrepareAsync_DependencyReportedOnlyByInstallInformation_IsAlsoProposed()
    {
        // Le mod demandé ne déclare rien, c'est resolve-deps qui signale la dépendance transitive.
        var harness = Create("1.22.0");
        harness.Server.ResolvedDependencies = ["vsimgui"];

        var plan = await harness.Service.PrepareAsync(Slug, 4400, cancellationToken: CancellationToken.None);

        plan.MissingDependencies.ShouldHaveSingleItem().ModIdString.ShouldBe("vsimgui");
        plan.Issues.ShouldHaveSingleItem().ReportedByModDb.ShouldBeTrue();
    }

    [Fact]
    public async Task PrepareAsync_DependencyAlreadyInstalled_IsNotProposedAgain()
    {
        var harness = Create();
        await InstallDependencyAsync(harness);

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        plan.MissingDependencies.ShouldBeEmpty();
        plan.Issues.ShouldBeEmpty();
        plan.NeedsConfirmation.ShouldBeFalse();
    }

    [Fact]
    public async Task PrepareAsync_DependencyUnknownToTheModDb_IsReportedAsUnresolvedRatherThanIgnored()
    {
        var harness = Create();
        harness.Server.KnownDependency = false;

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        plan.MissingDependencies.ShouldBeEmpty();
        plan.UnresolvedDependencies.ShouldBe(["vsimgui"]);
        plan.NeedsConfirmation.ShouldBeTrue();
    }

    [Fact]
    public async Task PrepareAsync_LeavesTheInstanceUntouchedUntilApplyIsCalled()
    {
        var harness = Create();

        await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        harness.FileSystem.Directory.GetFiles(harness.Repository.GetModsDirectory(Slug)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_UncheckedDependency_IsNeverInstalledSilently()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyAsync(Slug, plan, selectedDependencies: null, cancellationToken: CancellationToken.None);

        outcome.Installed.ShouldHaveSingleItem().Identity.ShouldBe("configlib");
        outcome.SkippedDependencies.ShouldBe(["vsimgui"]);
    }

    [Fact]
    public async Task ApplyAsync_CheckedDependency_IsInstalledAlongsideTheMod()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyAsync(Slug, plan, ["vsimgui"], cancellationToken: CancellationToken.None);

        outcome.Installed.Select(mod => mod.Identity).ShouldBe(["configlib", "vsimgui"]);
        outcome.SkippedDependencies.ShouldBeEmpty();
        harness.FileSystem.File
            .Exists(harness.FileSystem.Path.Combine(harness.Repository.GetModsDirectory(Slug), "vsimgui-1.3.0.zip"))
            .ShouldBeTrue();
    }

    // ── Garde-fou de taille, seul substitut à l'absence de checksum ──────────────────

    [Fact]
    public async Task PrepareAsync_TruncatedDownload_IsRejectedThankToTheAnnouncedContentLength()
    {
        var harness = Create();
        harness.Server.TruncateDownloads = true;

        await Should.ThrowAsync<ModInstallFailedException>(
            () => harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None));
    }

    // ── Désinstallation et vérification inverse ─────────────────────────────────────

    [Fact]
    public async Task PrepareUninstallAsync_ModThatOthersDependOn_NamesThem()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);
        await harness.Service.ApplyAsync(Slug, plan, ["vsimgui"], cancellationToken: CancellationToken.None);

        var installed = await harness.Repository.ScanAsync(Slug, CancellationToken.None);
        var target = installed.Single(mod => mod.Identity == "vsimgui");

        var impact = await harness.Service.PrepareUninstallAsync(Slug, target, CancellationToken.None);

        impact.HasDependents.ShouldBeTrue();
        impact.DependentNames.ShouldBe(["Config lib"]);
    }

    [Fact]
    public async Task PrepareUninstallAsync_ModNobodyDependsOn_HasNoWarning()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);
        await harness.Service.ApplyAsync(Slug, plan, ["vsimgui"], cancellationToken: CancellationToken.None);

        var installed = await harness.Repository.ScanAsync(Slug, CancellationToken.None);
        var target = installed.Single(mod => mod.Identity == "configlib");

        (await harness.Service.PrepareUninstallAsync(Slug, target, CancellationToken.None)).HasDependents.ShouldBeFalse();
    }

    [Fact]
    public async Task UninstallAsync_RemovesTheArchiveAndItsProvenance()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);
        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        await harness.Service.UninstallAsync(Slug, outcome.Installed[0], CancellationToken.None);

        (await harness.Repository.ScanAsync(Slug, CancellationToken.None)).ShouldBeEmpty();
        (await harness.Repository.LoadProvenanceAsync(Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task SetEnabledAsync_TogglesThroughTheRepositoryConvention()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);
        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var disabled = await harness.Service.SetEnabledAsync(Slug, outcome.Installed[0], enabled: false, CancellationToken.None);

        disabled.IsEnabled.ShouldBeFalse();
        disabled.FilePath.ShouldEndWith(".zip.disabled");
    }

    // ── Convention de nommage ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("configlib", "1.12.0", "configlib-1.12.0.zip")]
    [InlineData("carry_capacity", "1.8.0-rc.2", "carry_capacity-1.8.0-rc.2.zip")]
    [InlineData("../../evil", "1.0.0", "evil-1.0.0.zip")]
    [InlineData("!!!", "1.0.0", "mod-1.0.0.zip")]
    public void BuildFileName_KeepsASimpleNameThatCannotEscapeTheModsFolder(string modId, string version, string expected)
        => ModInstallService.BuildFileName(modId, ModVersion.Parse(version)).ShouldBe(expected);

    private static async Task InstallDependencyAsync(Harness harness)
    {
        var modsDirectory = harness.Repository.GetModsDirectory(Slug);
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(modsDirectory, "vsimgui-1.3.0.zip"),
            new MockFileData(FakeModDbServer.VsImGuiArchive));

        await Task.CompletedTask;
    }

    /// <summary>
    /// Serveur ModDB factice : trois fiches, leurs releases et les archives correspondantes, plus
    /// les leviers dont les tests ont besoin (dépendance inconnue, resolve-deps peuplé,
    /// téléchargement tronqué). Aucun appel réseau réel ne peut sortir d'ici.
    /// </summary>
    private sealed class FakeModDbServer
    {
        public static readonly byte[] ConfigLibArchive = ModInfoSamples.BuildArchive(ModInfoSamples.ConfigLib);
        public static readonly byte[] ExtraInfoArchive = ModInfoSamples.BuildArchive(ModInfoSamples.ExtraInfo);

        public static readonly byte[] VsImGuiArchive = ModInfoSamples.BuildArchive("""
        { "type": "code", "name": "VS ImGui", "modid": "vsimgui", "version": "1.3.0", "authors": ["Maltiez"] }
        """);

        /// <summary>Faux pour simuler une dépendance déclarée que le ModDB ne connaît pas.</summary>
        public bool KnownDependency { get; set; } = true;

        /// <summary>Identifiants que <c>resolve-deps</c> doit remonter.</summary>
        public IReadOnlyList<string> ResolvedDependencies { get; set; } = [];

        /// <summary>Vrai pour livrer moins d'octets que le <c>HEAD</c> n'en annonce.</summary>
        public bool TruncateDownloads { get; set; }

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var url = request.RequestUri!;

            if (url.Host == "moddbcdn.vintagestory.at")
            {
                return File(url.AbsolutePath, request.Method == HttpMethod.Head);
            }

            return url.AbsolutePath switch
            {
                "/api/mod/1783" or "/api/mod/configlib" => FakeHttpMessageHandler.Text(ConfigLibJson),
                "/api/mod/4400" or "/api/mod/extrainfo" => FakeHttpMessageHandler.Text(ExtraInfoJson),
                "/api/mod/vsimgui" when KnownDependency => FakeHttpMessageHandler.Text(VsImGuiJson),
                "/api/v2/mods/install-information" => FakeHttpMessageHandler.Text(InstallInformationJson(url.Query)),
                _ => FakeHttpMessageHandler.Text(ModDbSamples.NotFound),
            };
        }

        private HttpResponseMessage File(string path, bool headOnly)
        {
            var payload = path switch
            {
                "/configlib_1.11.1.zip" => ConfigLibArchive,
                "/extrainfo_2.2.1.zip" => ExtraInfoArchive,
                "/vsimgui_1.3.0.zip" => VsImGuiArchive,
                _ => null,
            };

            if (payload is null)
            {
                return FakeHttpMessageHandler.Status(HttpStatusCode.NotFound);
            }

            var body = !headOnly && TruncateDownloads ? payload[..(payload.Length / 2)] : payload;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            if (headOnly)
            {
                response.Content.Headers.ContentLength = payload.Length;
            }

            return response;
        }

        // Le vrai serveur ne met dans `data` que les identifiants explicitement demandés, et place
        // les dépendances transitives dans `resolved` : le faux fait pareil, sinon il testerait un
        // contrat qui n'existe pas.
        private string InstallInformationJson(string query)
        {
            var requested = System.Web.HttpUtility.ParseQueryString(query)["ids"] ?? string.Empty;
            var resolved = string.Join(
                ',',
                ResolvedDependencies.Select(identifier => $$"""
                "{{identifier}}": { "version": "1.3.0", "fileName": "{{identifier}}.zip", "fileUrl": "/download/1/{{identifier}}.zip" }
                """));

            return $$"""
            { "data": { "{{requested}}": { "recommendedUpgrade": "1.12.0" } }, "resolved": { {{resolved}} } }
            """;
        }

        private const string ConfigLibJson = """
        {
          "statuscode": "200",
          "mod": {
            "modid": 1783, "assetid": 9551, "name": "Config lib", "text": "<p>lib</p>", "author": "Maltiez",
            "urlalias": null, "logofile": null, "downloads": 627953, "side": "both", "type": "mod",
            "tags": ["Utility"], "lastreleased": "2026-05-01 12:03:34",
            "releases": [
              { "releaseid": 39980, "fileid": 88961, "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.12.0.zip",
                "filename": "configlib_1.12.0.zip", "downloads": 1, "tags": ["1.22.0"], "modidstr": "configlib",
                "modversion": "1.12.0", "changelog": null, "created": "2026-05-01 12:03:34" },
              { "releaseid": 38314, "fileid": 84120, "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.11.1.zip",
                "filename": "configlib_1.11.1.zip", "downloads": 1, "tags": ["1.21.3", "1.21.0"], "modidstr": "configlib",
                "modversion": "1.11.1", "changelog": null, "created": "2026-02-11 09:22:10" }
            ]
          }
        }
        """;

        private const string ExtraInfoJson = """
        {
          "statuscode": "200",
          "mod": {
            "modid": 4400, "assetid": 12000, "name": "Extra Info", "text": "<p>info</p>", "author": "Craluminum2413",
            "urlalias": null, "logofile": null, "downloads": 174994, "side": "client", "type": "mod",
            "tags": [], "lastreleased": "2026-04-01 10:00:00",
            "releases": [
              { "releaseid": 40100, "fileid": 90000, "mainfile": "https://moddbcdn.vintagestory.at/extrainfo_2.2.1.zip",
                "filename": "ExtraInfo-v2.2.1.zip", "downloads": 1, "tags": ["1.22.0"], "modidstr": "extrainfo",
                "modversion": "2.2.1", "changelog": null, "created": "2026-04-01 10:00:00" }
            ]
          }
        }
        """;

        private const string VsImGuiJson = """
        {
          "statuscode": "200",
          "mod": {
            "modid": 2000, "assetid": 8000, "name": "VS ImGui", "text": "", "author": "Maltiez",
            "urlalias": null, "logofile": null, "downloads": 100, "side": "client", "type": "mod",
            "tags": [], "lastreleased": "2026-01-01 10:00:00",
            "releases": [
              { "releaseid": 30000, "fileid": 70001, "mainfile": "https://moddbcdn.vintagestory.at/vsimgui_1.3.0.zip",
                "filename": "vsimgui_1.3.0.zip", "downloads": 1, "tags": ["1.21.3", "1.21.9", "1.22.0"], "modidstr": "vsimgui",
                "modversion": "1.3.0", "changelog": null, "created": "2026-01-01 10:00:00" }
            ]
          }
        }
        """;
    }
}