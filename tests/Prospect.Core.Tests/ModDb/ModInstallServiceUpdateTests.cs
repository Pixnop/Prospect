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
/// Mise à jour d'un mod déjà installé : <see cref="ModInstallService.PrepareUpdateAsync"/>,
/// <see cref="ModInstallService.ApplyUpdateAsync"/> et <see cref="ModInstallService.ApplyAllUpdatesAsync"/>.
/// Harnais séparé de <see cref="ModInstallServiceTests"/> (plutôt qu'un serveur factice partagé) pour
/// ne prendre aucun risque sur les archives/releases déjà utilisées par ses tests existants.
/// </summary>
public sealed class ModInstallServiceUpdateTests
{
    private const string Slug = "homestead-121";

    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        ModInstallService Service,
        IInstalledModRepository Repository,
        MockFileSystem FileSystem,
        FakeServer Server);

    private static Harness Create(string gameVersion = "1.22.1")
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
        server.CdnFiles["/configlib_1.12.0.zip"] = ModInfoSamples.BuildArchive(ModInfo("configlib", "Config lib", "1.12.0"));

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

    private static async Task<InstalledMod> SeedInstalledConfigLibAsync(
        Harness harness, string version = "1.0.0", bool enabled = true, bool withProvenance = true)
    {
        var modsDirectory = harness.Repository.GetModsDirectory(Slug);
        var stableName = $"configlib-{version}.zip";
        var diskName = enabled ? stableName : stableName + ".disabled";
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(modsDirectory, diskName),
            new MockFileData(ModInfoSamples.BuildArchive(ModInfo("configlib", "Config lib", version))));

        if (withProvenance)
        {
            await harness.Repository.SaveProvenanceAsync(
                Slug,
                new ModProvenance
                {
                    FileName = stableName,
                    ModId = 1783,
                    ModIdString = "configlib",
                    ReleaseId = 100,
                    FileId = 100,
                    Version = ModVersion.Parse(version),
                    InstalledUtc = Noon,
                },
                CancellationToken.None);
        }

        var installed = await harness.Repository.ScanAsync(Slug, CancellationToken.None);

        return installed.Single(mod => mod.Identity == "configlib");
    }

    private static string ModInfo(string modId, string name, string version, string? dependency = null)
    {
        var dependencies = dependency is null ? "{}" : $$"""{ "{{dependency}}": "" }""";

        return $$"""
        { "type": "code", "modid": "{{modId}}", "name": "{{name}}", "version": "{{version}}",
          "authors": ["Quelqu'un"], "dependencies": {{dependencies}} }
        """;
    }

    private static ModUpdateResult UpdateResultFor(InstalledMod mod, string targetVersion = "1.12.0", bool approximate = false) => new(
        mod,
        ModUpdateStatus.UpdateAvailable,
        new ModDbRelease
        {
            ReleaseId = 39980,
            FileId = 88961,
            ModIdString = "configlib",
            Version = ModVersion.Parse(targetVersion),
            FileName = $"configlib_{targetVersion}.zip",
            DownloadUrl = new Uri($"https://moddbcdn.vintagestory.at/configlib_{targetVersion}.zip"),
        },
        approximate,
        null);

    // ── Remplacement ordonné, état préservé, provenance ─────────────────────────────

    [Fact]
    public async Task ApplyUpdateAsync_WritesTheNewVersionAndRemovesTheOldOne()
    {
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness);
        var plan = await harness.Service.PrepareUpdateAsync(Slug, UpdateResultFor(previous), cancellationToken: CancellationToken.None);

        await harness.Service.ApplyUpdateAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var modsDirectory = harness.Repository.GetModsDirectory(Slug);
        harness.FileSystem.File.Exists(harness.FileSystem.Path.Combine(modsDirectory, "configlib-1.12.0.zip")).ShouldBeTrue();
        harness.FileSystem.File.Exists(harness.FileSystem.Path.Combine(modsDirectory, "configlib-1.0.0.zip")).ShouldBeFalse();
    }

    [Fact]
    public async Task PrepareUpdateAsync_DownloadOfTheNewVersionFails_NeverTouchesTheOldFile()
    {
        // La release annoncée n'a délibérément aucun fichier CDN correspondant : le téléchargement
        // échoue avant même que PrepareUpdateAsync ne rende un plan, donc ApplyUpdateAsync n'est
        // jamais appelée. C'est la garantie « jamais l'inverse » vue depuis son point de départ :
        // rien ne peut supprimer l'ancien fichier tant que le nouveau n'a pas ne serait-ce que fini
        // de se télécharger.
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness);
        var missingRelease = UpdateResultFor(previous, "9.9.9");

        await Should.ThrowAsync<DownloadFailedException>(
            () => harness.Service.PrepareUpdateAsync(Slug, missingRelease, cancellationToken: CancellationToken.None));

        var modsDirectory = harness.Repository.GetModsDirectory(Slug);
        harness.FileSystem.File.Exists(harness.FileSystem.Path.Combine(modsDirectory, "configlib-1.0.0.zip")).ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyUpdateAsync_PreviousWasDisabled_TheNewFileIsDisabledToo()
    {
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness, enabled: false);
        previous.IsEnabled.ShouldBeFalse();
        var plan = await harness.Service.PrepareUpdateAsync(Slug, UpdateResultFor(previous), cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyUpdateAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var updated = outcome.Installed.ShouldHaveSingleItem();
        updated.IsEnabled.ShouldBeFalse();
        updated.FilePath.ShouldEndWith(".zip.disabled");
    }

    [Fact]
    public async Task ApplyUpdateAsync_PreviousWasEnabled_TheNewFileStaysEnabled()
    {
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness);
        var plan = await harness.Service.PrepareUpdateAsync(Slug, UpdateResultFor(previous), cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyUpdateAsync(Slug, plan, cancellationToken: CancellationToken.None);

        outcome.Installed.ShouldHaveSingleItem().IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyUpdateAsync_ReplacesTheProvenanceRatherThanLeavingTheOldEntryBehind()
    {
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness);
        var plan = await harness.Service.PrepareUpdateAsync(Slug, UpdateResultFor(previous), cancellationToken: CancellationToken.None);

        await harness.Service.ApplyUpdateAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var provenance = await harness.Repository.LoadProvenanceAsync(Slug, CancellationToken.None);
        provenance.ShouldNotContainKey("configlib-1.0.0.zip");
        var entry = provenance["configlib-1.12.0.zip"];
        entry.Version.ShouldBe(ModVersion.Parse("1.12.0"));
        entry.ReleaseId.ShouldBe(39980);
        entry.ModId.ShouldBe(1783);
    }

    // ── Nouvelles dépendances, dépendants informatifs ────────────────────────────────

    [Fact]
    public async Task PrepareUpdateAsync_NewVersionDeclaresANewDependency_IsProposedInThePlan()
    {
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness);
        // La NOUVELLE version (celle téléchargée) déclare une dépendance que l'ANCIENNE n'avait pas.
        harness.Server.CdnFiles["/configlib_1.12.0.zip"] = ModInfoSamples.BuildArchive(ModInfo("configlib", "Config lib", "1.12.0", dependency: "vsimgui"));
        harness.Server.ModDetailJson["/api/mod/vsimgui"] = BuildModDetailJson(2000, "VS ImGui", "vsimgui", "1.3.0", "vsimgui_1.3.0.zip");
        harness.Server.CdnFiles["/vsimgui_1.3.0.zip"] = ModInfoSamples.BuildArchive("""{ "modid": "vsimgui", "name": "VS ImGui", "version": "1.3.0" }""");

        var plan = await harness.Service.PrepareUpdateAsync(Slug, UpdateResultFor(previous), cancellationToken: CancellationToken.None);

        plan.MissingDependencies.ShouldHaveSingleItem().ModIdString.ShouldBe("vsimgui");
        plan.NeedsConfirmation.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyUpdateAsync_NewDependencyLeftUnchecked_IsNeverInstalledSilently()
    {
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness);
        harness.Server.CdnFiles["/configlib_1.12.0.zip"] = ModInfoSamples.BuildArchive(ModInfo("configlib", "Config lib", "1.12.0", dependency: "vsimgui"));
        harness.Server.ModDetailJson["/api/mod/vsimgui"] = BuildModDetailJson(2000, "VS ImGui", "vsimgui", "1.3.0", "vsimgui_1.3.0.zip");
        harness.Server.CdnFiles["/vsimgui_1.3.0.zip"] = ModInfoSamples.BuildArchive("""{ "modid": "vsimgui", "name": "VS ImGui", "version": "1.3.0" }""");
        var plan = await harness.Service.PrepareUpdateAsync(Slug, UpdateResultFor(previous), cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyUpdateAsync(Slug, plan, selectedDependencies: null, cancellationToken: CancellationToken.None);

        outcome.Installed.ShouldHaveSingleItem().Identity.ShouldBe("configlib");
        outcome.SkippedDependencies.ShouldBe(["vsimgui"]);
    }

    [Fact]
    public async Task PrepareUpdateAsync_OtherInstalledModsDependOnIt_ListsThemInformativelyWithoutBlocking()
    {
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness);
        var modsDirectory = harness.Repository.GetModsDirectory(Slug);
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(modsDirectory, "carrycapacity-1.0.0.zip"),
            new MockFileData(ModInfoSamples.BuildArchive(ModInfo("carrycapacity", "Carry Capacity", "1.0.0", dependency: "configlib"))));

        var plan = await harness.Service.PrepareUpdateAsync(Slug, UpdateResultFor(previous), cancellationToken: CancellationToken.None);

        plan.HasDependents.ShouldBeTrue();
        plan.DependentNames.ShouldBe(["Carry Capacity"]);

        // Informatif : rien n'empêche d'obtenir le plan ni de l'appliquer.
        var outcome = await harness.Service.ApplyUpdateAsync(Slug, plan, cancellationToken: CancellationToken.None);
        outcome.Installed.ShouldHaveSingleItem().Identity.ShouldBe("configlib");
    }

    [Fact]
    public async Task PrepareUpdateAsync_NobodyDependsOnIt_HasNoDependents()
    {
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness);

        var plan = await harness.Service.PrepareUpdateAsync(Slug, UpdateResultFor(previous), cancellationToken: CancellationToken.None);

        plan.Dependents.ShouldBeEmpty();
        plan.HasDependents.ShouldBeFalse();
    }

    // ── Résolution de l'identifiant numérique sans provenance préalable ─────────────

    [Fact]
    public async Task PrepareUpdateAsync_NoPriorProvenance_ResolvesTheNumericModDbIdFromTheApi()
    {
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness, withProvenance: false);
        harness.Server.ModDetailJson["/api/mod/configlib"] = BuildModDetailJson(1783, "Config lib", "configlib", "1.12.0", "configlib_1.12.0.zip");

        var plan = await harness.Service.PrepareUpdateAsync(Slug, UpdateResultFor(previous), cancellationToken: CancellationToken.None);
        var outcome = await harness.Service.ApplyUpdateAsync(Slug, plan, cancellationToken: CancellationToken.None);

        (await harness.Repository.LoadProvenanceAsync(Slug, CancellationToken.None))["configlib-1.12.0.zip"].ModId.ShouldBe(1783);
        outcome.Installed.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task PrepareUpdateAsync_NoPriorProvenanceAndApiUnreachableForTheId_StillCompletesTheUpdate()
    {
        // L'identifiant numérique n'est qu'un confort de bookkeeping (jamais relu ailleurs) : son
        // indisponibilité ne doit jamais empêcher une mise à jour par ailleurs prête.
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness, withProvenance: false);
        // Pas d'entrée /api/mod/configlib dans ModDetailJson : NotFound (statuscode 404).

        var plan = await harness.Service.PrepareUpdateAsync(Slug, UpdateResultFor(previous), cancellationToken: CancellationToken.None);

        plan.Updated.ModDbModId.ShouldBe(0);
    }

    [Fact]
    public async Task PrepareUpdateAsync_WithoutAnAvailableRelease_Throws()
    {
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness);
        var noUpdate = new ModUpdateResult(previous, ModUpdateStatus.UpToDate);

        await Should.ThrowAsync<ArgumentException>(
            () => harness.Service.PrepareUpdateAsync(Slug, noUpdate, cancellationToken: CancellationToken.None));
    }

    // ── Tout mettre à jour ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAllUpdatesAsync_AppliesEveryAvailableUpdateSequentially()
    {
        var harness = Create();
        var configlib = await SeedInstalledConfigLibAsync(harness);
        var modsDirectory = harness.Repository.GetModsDirectory(Slug);
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(modsDirectory, "carrycapacity-1.0.0.zip"),
            new MockFileData(ModInfoSamples.BuildArchive(ModInfo("carrycapacity", "Carry Capacity", "1.0.0"))));
        harness.Server.CdnFiles["/carrycapacity_1.5.0.zip"] = ModInfoSamples.BuildArchive(ModInfo("carrycapacity", "Carry Capacity", "1.5.0"));
        var installed = await harness.Repository.ScanAsync(Slug, CancellationToken.None);
        var carryCapacity = installed.Single(mod => mod.Identity == "carrycapacity");

        var updates = new List<ModUpdateResult>
        {
            UpdateResultFor(configlib),
            CarryCapacityUpdateResult(carryCapacity, "1.5.0"),
        };

        var outcome = await harness.Service.ApplyAllUpdatesAsync(Slug, updates, cancellationToken: CancellationToken.None);

        outcome.Updated.Count.ShouldBe(2);
        outcome.Failed.ShouldBeEmpty();
        var remaining = await harness.Repository.ScanAsync(Slug, CancellationToken.None);
        remaining.Select(mod => mod.FileName).ShouldBe(["carrycapacity-1.5.0.zip", "configlib-1.12.0.zip"], ignoreOrder: true);
    }

    [Fact]
    public async Task ApplyAllUpdatesAsync_ReportsAggregateProgressPerModRatherThanPerByte()
    {
        var harness = Create();
        var configlib = await SeedInstalledConfigLibAsync(harness);
        var reports = new List<BulkUpdateProgress>();

        // SynchronousProgress, pas Progress<T> : ce dernier poste ses rappels sur le contexte de
        // synchronisation capturé, donc APRÈS le retour de l'appel dans un test sans contexte. Les
        // assertions couraient contre le pool de threads et ce test échouait au hasard en CI. Tout
        // le reste de la suite utilise déjà ce double pour cette raison exacte.
        await harness.Service.ApplyAllUpdatesAsync(
            Slug,
            [UpdateResultFor(configlib)],
            new SynchronousProgress<BulkUpdateProgress>(reports.Add),
            CancellationToken.None);

        reports.ShouldContain(report => report.CompletedCount == 0 && report.TotalCount == 1 && report.CurrentModName == "Config lib");
        reports.ShouldContain(report => report.CompletedCount == 1 && report.TotalCount == 1);
    }

    [Fact]
    public async Task ApplyAllUpdatesAsync_OneModFails_OthersStillGetApplied()
    {
        var harness = Create();
        var configlib = await SeedInstalledConfigLibAsync(harness);
        var modsDirectory = harness.Repository.GetModsDirectory(Slug);
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(modsDirectory, "carrycapacity-1.0.0.zip"),
            new MockFileData(ModInfoSamples.BuildArchive(ModInfo("carrycapacity", "Carry Capacity", "1.0.0"))));
        // Pas de fichier CDN pour carrycapacity_1.5.0.zip : son téléchargement échoue.
        var installed = await harness.Repository.ScanAsync(Slug, CancellationToken.None);
        var carryCapacity = installed.Single(mod => mod.Identity == "carrycapacity");

        var outcome = await harness.Service.ApplyAllUpdatesAsync(
            Slug,
            [CarryCapacityUpdateResult(carryCapacity, "1.5.0"), UpdateResultFor(configlib)],
            cancellationToken: CancellationToken.None);

        outcome.Failed.ShouldHaveSingleItem().ModName.ShouldBe("Carry Capacity");
        outcome.Updated.ShouldHaveSingleItem().Identity.ShouldBe("configlib");
    }

    [Fact]
    public async Task ApplyAllUpdatesAsync_CancellationRequestedBeforeTheSecondMod_StopsCleanlyBetweenReplacements()
    {
        var harness = Create();
        var configlib = await SeedInstalledConfigLibAsync(harness);
        var modsDirectory = harness.Repository.GetModsDirectory(Slug);
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(modsDirectory, "carrycapacity-1.0.0.zip"),
            new MockFileData(ModInfoSamples.BuildArchive(ModInfo("carrycapacity", "Carry Capacity", "1.0.0"))));
        harness.Server.CdnFiles["/carrycapacity_1.5.0.zip"] = ModInfoSamples.BuildArchive(ModInfo("carrycapacity", "Carry Capacity", "1.5.0"));
        var installed = await harness.Repository.ScanAsync(Slug, CancellationToken.None);
        var carryCapacity = installed.Single(mod => mod.Identity == "carrycapacity");

        using var cts = new CancellationTokenSource();
        // Synchrone pour la même raison qu'au-dessus, et ici c'est le cœur du scénario : l'annulation
        // doit tomber pendant le premier mod, pas quand le pool de threads voudra bien la livrer.
        var progress = new SynchronousProgress<BulkUpdateProgress>(report =>
        {
            // Le premier mod vient de démarrer : on annule immédiatement, mais ce mod doit quand
            // même se terminer proprement avant que la boucle ne s'arrête.
            if (report.CompletedCount == 0)
            {
                cts.Cancel();
            }
        });

        var outcome = await harness.Service.ApplyAllUpdatesAsync(
            Slug,
            [UpdateResultFor(configlib), CarryCapacityUpdateResult(carryCapacity, "1.5.0")],
            progress,
            cts.Token);

        outcome.Updated.ShouldHaveSingleItem().Identity.ShouldBe("configlib");
        (await harness.Repository.ScanAsync(Slug, CancellationToken.None))
            .ShouldContain(mod => mod.Identity == "carrycapacity" && mod.Version == ModVersion.Parse("1.0.0"));
    }

    [Fact]
    public async Task ApplyAllUpdatesAsync_IgnoresResultsThatAreAlreadyUpToDate()
    {
        var harness = Create();
        var previous = await SeedInstalledConfigLibAsync(harness);

        var outcome = await harness.Service.ApplyAllUpdatesAsync(
            Slug,
            [new ModUpdateResult(previous, ModUpdateStatus.UpToDate)],
            cancellationToken: CancellationToken.None);

        outcome.Updated.ShouldBeEmpty();
        outcome.Failed.ShouldBeEmpty();
    }

    private static ModUpdateResult CarryCapacityUpdateResult(InstalledMod mod, string targetVersion) => new(
        mod,
        ModUpdateStatus.UpdateAvailable,
        new ModDbRelease
        {
            ReleaseId = 1,
            FileId = 1,
            ModIdString = "carrycapacity",
            Version = ModVersion.Parse(targetVersion),
            FileName = $"carrycapacity_{targetVersion}.zip",
            DownloadUrl = new Uri($"https://moddbcdn.vintagestory.at/carrycapacity_{targetVersion}.zip"),
        },
        false,
        null);

    private static string BuildModDetailJson(int modId, string name, string modIdString, string version, string fileName) => $$"""
    {
      "statuscode": "200",
      "mod": {
        "modid": {{modId}}, "assetid": 1, "name": "{{name}}", "text": "", "author": "Quelqu'un",
        "urlalias": null, "logofile": null, "downloads": 1, "side": "both", "type": "mod",
        "tags": [], "lastreleased": "2026-01-01 10:00:00",
        "releases": [
          { "releaseid": 1, "fileid": 1, "mainfile": "https://moddbcdn.vintagestory.at/{{fileName}}",
            "filename": "{{fileName}}", "downloads": 1, "tags": ["1.22.1", "1.22.0"], "modidstr": "{{modIdString}}",
            "modversion": "{{version}}", "changelog": null, "created": "2026-01-01 10:00:00" }
        ]
      }
    }
    """;

    /// <summary>
    /// Serveur ModDB factice dédié aux scénarios de mise à jour : fichiers CDN et fiches
    /// <c>/api/mod/{id}</c> ajoutables à la volée, <c>install-information</c> vide par défaut.
    /// </summary>
    private sealed class FakeServer
    {
        public Dictionary<string, byte[]> CdnFiles { get; } = [];

        public Dictionary<string, string> ModDetailJson { get; } = [];

        public string InstallInformationJson { get; set; } = """{ "data": {} }""";

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
                return FakeHttpMessageHandler.Text(InstallInformationJson);
            }

            return ModDetailJson.TryGetValue(url.AbsolutePath, out var json)
                ? FakeHttpMessageHandler.Text(json)
                : FakeHttpMessageHandler.Text(ModDbSamples.NotFound);
        }
    }
}