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
using Prospect.Core.Tests.Instances;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

/// <summary>
/// Installer un mod DÉJÀ installé, depuis le navigateur : réinstallation à l'identique, montée de
/// version, et retour à une version antérieure par le sélecteur de release de la fiche.
/// </summary>
/// <remarks>
/// Le défaut d'origine : le plan d'installation ne regardait jamais si le mod était déjà là, et le
/// nom de fichier cible porte la version. Changer de version posait donc un SECOND zip à côté du
/// premier, deux fois le même <c>modid</c> dans <c>Mods/</c>, ce dont le comportement au chargement
/// du jeu n'est défini nulle part. Le plan porte maintenant la copie existante et l'application
/// suit la discipline de la mise à jour.
/// </remarks>
public sealed class ModInstallServiceReplaceTests
{
    private const string Slug = "homestead-121";
    private const int ConfigLibModId = 1783;

    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(ModInstallService Service, IInstalledModRepository Repository, MockFileSystem FileSystem, FakeServer Server);

    private static Harness Create(string gameVersion = "1.22.1")
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Noon);
        var store = new JsonFileStore(fileSystem);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, store, new InstanceMetadataMigrationPipeline([]));
        var archiveReader = new ModArchiveReader(fileSystem);
        var repository = new FileSystemInstalledModRepository(fileSystem, instances, archiveReader, new DisabledSuffixModStateConvention(), store);

        var server = new FakeServer();
        var handler = new FakeHttpMessageHandler(server.Respond);
        var client = new ModDbClient(
            new HttpClient(handler),
            store,
            Paths,
            clock,
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask));
        var downloads = new DownloadManager(new HttpClient(handler), fileSystem, Paths, clock);

        SeedInstance(fileSystem, gameVersion);

        foreach (var version in new[] { "1.10.0", "1.11.1", "1.12.0" })
        {
            server.CdnFiles[$"/configlib_{version}.zip"] = ModInfoSamples.BuildArchive(ModInfo("configlib", "Config lib", version));
        }

        server.ModDetailJson[$"/api/mod/{ConfigLibModId}"] = BuildModDetailJson("Config lib", "configlib", ["1.10.0", "1.11.1", "1.12.0"]);

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

    private static async Task SeedInstalledAsync(Harness harness, string version, bool enabled = true, bool withProvenance = true)
    {
        var modsDirectory = harness.Repository.GetModsDirectory(Slug);
        var stableName = $"configlib-{version}.zip";
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(modsDirectory, enabled ? stableName : stableName + ".disabled"),
            new MockFileData(ModInfoSamples.BuildArchive(ModInfo("configlib", "Config lib", version))));

        if (withProvenance)
        {
            await harness.Repository.SaveProvenanceAsync(
                Slug,
                new ModProvenance
                {
                    FileName = stableName,
                    ModId = ConfigLibModId,
                    ModIdString = "configlib",
                    ReleaseId = 100,
                    FileId = 100,
                    Version = ModVersion.Parse(version),
                    InstalledUtc = Noon,
                },
                CancellationToken.None);
        }
    }

    private static int? ReleaseIdFor(ModInstallPlan plan, string version)
        => plan.AvailableReleases.FirstOrDefault(choice => choice.Release.Version == ModVersion.Parse(version))?.Release.ReleaseId;

    private static string[] ModFiles(Harness harness)
        => [.. harness.FileSystem.Directory
            .GetFiles(harness.Repository.GetModsDirectory(Slug))
            .Select(path => harness.FileSystem.Path.GetFileName(path))
            .Order(StringComparer.Ordinal)];

    // ── Le plan reconnaît la copie déjà installée ────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_NothingInstalledYet_IsNotAReplacement()
    {
        var harness = Create();

        var plan = await harness.Service.PrepareAsync(Slug, ConfigLibModId, cancellationToken: CancellationToken.None);

        plan.IsReplacement.ShouldBeFalse();
        plan.Existing.ShouldBeNull();
        plan.ExistingVersion.ShouldBeNull();
    }

    [Fact]
    public async Task PrepareAsync_ModAlreadyInstalled_CarriesItAndItsVersion()
    {
        var harness = Create();
        await SeedInstalledAsync(harness, "1.10.0");

        var plan = await harness.Service.PrepareAsync(Slug, ConfigLibModId, cancellationToken: CancellationToken.None);

        plan.IsReplacement.ShouldBeTrue();
        plan.Existing!.FileName.ShouldBe("configlib-1.10.0.zip");
        plan.ExistingVersion.ShouldBe(ModVersion.Parse("1.10.0"));
    }

    /// <summary>
    /// Un zip déposé à la main par le joueur n'a aucune provenance : la correspondance retombe sur
    /// le <c>modid</c> du <c>modinfo.json</c>, parce que le disque est la vérité et que le fichier
    /// de provenance n'est qu'un cache (docs/architecture.md).
    /// </summary>
    [Fact]
    public async Task PrepareAsync_ModDroppedByHandWithoutProvenance_IsStillRecognised()
    {
        var harness = Create();
        await SeedInstalledAsync(harness, "1.10.0", withProvenance: false);

        var plan = await harness.Service.PrepareAsync(Slug, ConfigLibModId, cancellationToken: CancellationToken.None);

        plan.IsReplacement.ShouldBeTrue();
        plan.Existing!.FileName.ShouldBe("configlib-1.10.0.zip");
    }

    // ── Les trois cas de remplacement ────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_Upgrade_LeavesExactlyOneZipAndOneProvenanceEntry()
    {
        var harness = Create();
        await SeedInstalledAsync(harness, "1.10.0");
        var plan = await harness.Service.PrepareAsync(Slug, ConfigLibModId, cancellationToken: CancellationToken.None);

        await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        ModFiles(harness).ShouldBe(["configlib-1.12.0.zip"]);
        var provenance = await harness.Repository.LoadProvenanceAsync(Slug, CancellationToken.None);
        provenance.Keys.ShouldBe(["configlib-1.12.0.zip"]);
        provenance["configlib-1.12.0.zip"].Version.ShouldBe(ModVersion.Parse("1.12.0"));
    }

    [Fact]
    public async Task ApplyAsync_Downgrade_LeavesExactlyOneZipAndOneProvenanceEntry()
    {
        var harness = Create();
        await SeedInstalledAsync(harness, "1.12.0");
        var probe = await harness.Service.PrepareAsync(Slug, ConfigLibModId, cancellationToken: CancellationToken.None);
        var older = ReleaseIdFor(probe, "1.10.0").ShouldNotBeNull();

        var plan = await harness.Service.PrepareAsync(Slug, ConfigLibModId, releaseId: older, cancellationToken: CancellationToken.None);
        plan.Primary.Version.ShouldBe(ModVersion.Parse("1.10.0"));

        await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        ModFiles(harness).ShouldBe(["configlib-1.10.0.zip"]);
        var provenance = await harness.Repository.LoadProvenanceAsync(Slug, CancellationToken.None);
        provenance.Keys.ShouldBe(["configlib-1.10.0.zip"]);
    }

    [Fact]
    public async Task ApplyAsync_ReinstallOfTheSameVersion_KeepsASingleFile()
    {
        var harness = Create();
        await SeedInstalledAsync(harness, "1.12.0");
        var plan = await harness.Service.PrepareAsync(Slug, ConfigLibModId, cancellationToken: CancellationToken.None);
        plan.Primary.Version.ShouldBe(ModVersion.Parse("1.12.0"));

        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        ModFiles(harness).ShouldBe(["configlib-1.12.0.zip"]);
        outcome.Installed.ShouldHaveSingleItem().IsEnabled.ShouldBeTrue();
    }

    // ── État activé/désactivé, discipline d'ordre ────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_ReplacingADisabledMod_KeepsItDisabled()
    {
        var harness = Create();
        await SeedInstalledAsync(harness, "1.10.0", enabled: false);
        var plan = await harness.Service.PrepareAsync(Slug, ConfigLibModId, cancellationToken: CancellationToken.None);
        plan.Existing!.IsEnabled.ShouldBeFalse();

        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        ModFiles(harness).ShouldBe(["configlib-1.12.0.zip.disabled"]);
        outcome.Installed.ShouldHaveSingleItem().IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task ApplyAsync_ReinstallingADisabledModAtTheSameVersion_StaysDisabledAndSingle()
    {
        var harness = Create();
        await SeedInstalledAsync(harness, "1.12.0", enabled: false);
        var plan = await harness.Service.PrepareAsync(Slug, ConfigLibModId, cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        ModFiles(harness).ShouldBe(["configlib-1.12.0.zip.disabled"]);
        outcome.Installed.ShouldHaveSingleItem().IsEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// La discipline vue depuis son point de départ : le remplacement ne commence qu'une fois le
    /// nouveau fichier téléchargé, donc un téléchargement impossible laisse l'existant intact.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_WhenTheNewFileCannotBeDownloaded_NeverTouchesTheInstalledCopy()
    {
        var harness = Create();
        await SeedInstalledAsync(harness, "1.10.0");
        harness.Server.CdnFiles.Remove("/configlib_1.12.0.zip");

        await Should.ThrowAsync<DownloadFailedException>(
            () => harness.Service.PrepareAsync(Slug, ConfigLibModId, cancellationToken: CancellationToken.None));

        ModFiles(harness).ShouldBe(["configlib-1.10.0.zip"]);
    }

    private static string ModInfo(string modId, string name, string version) => $$"""
    { "type": "code", "modid": "{{modId}}", "name": "{{name}}", "version": "{{version}}",
      "authors": ["Quelqu'un"], "dependencies": {} }
    """;

    private static string BuildModDetailJson(string name, string modIdString, IReadOnlyList<string> versions)
    {
        var releases = versions
            .Select((version, index) => $$"""
              { "releaseid": {{100 + index}}, "fileid": {{200 + index}}, "mainfile": "https://moddbcdn.vintagestory.at/{{modIdString}}_{{version}}.zip",
                "filename": "{{modIdString}}_{{version}}.zip", "downloads": 1, "tags": ["1.22.1", "1.22.0"], "modidstr": "{{modIdString}}",
                "modversion": "{{version}}", "changelog": null, "created": "2026-01-0{{index + 1}} 10:00:00" }
            """);

        return $$"""
        {
          "statuscode": "200",
          "mod": {
            "modid": {{ConfigLibModId}}, "assetid": 1, "name": "{{name}}", "text": "", "author": "Quelqu'un",
            "urlalias": null, "logofile": null, "downloads": 1, "side": "both", "type": "mod",
            "tags": [], "lastreleased": "2026-01-01 10:00:00",
            "releases": [
        {{string.Join(",\n", releases)}}
            ]
          }
        }
        """;
    }

    private sealed class FakeServer
    {
        public Dictionary<string, byte[]> CdnFiles { get; } = [];

        public Dictionary<string, string> ModDetailJson { get; } = [];

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var url = request.RequestUri!;

            if (url.Host == "moddbcdn.vintagestory.at")
            {
                if (!CdnFiles.TryGetValue(url.AbsolutePath, out var bytes))
                {
                    return FakeHttpMessageHandler.Status(HttpStatusCode.NotFound);
                }

                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                if (request.Method == HttpMethod.Head)
                {
                    response.Content.Headers.ContentLength = bytes.Length;
                }

                return response;
            }

            if (url.AbsolutePath == "/api/v2/mods/install-information")
            {
                return FakeHttpMessageHandler.Text("""{ "data": {} }""");
            }

            return ModDetailJson.TryGetValue(url.AbsolutePath, out var json)
                ? FakeHttpMessageHandler.Text(json)
                : FakeHttpMessageHandler.Text(ModDbSamples.NotFound);
        }
    }
}