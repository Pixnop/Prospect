using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Http;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Modpacks;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.GameVersions;
using Prospect.Core.Tests.Http;
using Prospect.Core.Tests.Instances;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Modpacks;

public sealed class ModpackImportServiceTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        ModpackImportService Service,
        IInstanceRepository Instances,
        IInstalledModRepository Mods,
        IInstalledGameVersionRepository GameVersions,
        MockFileSystem FileSystem,
        ModpackTestServer Server,
        FakeGameInstallStrategy Strategy);

    private static Harness Create(bool gameVersionPreinstalled = true)
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Noon);
        var store = new JsonFileStore(fileSystem);
        var instanceRepository = new FileSystemInstanceRepository(fileSystem, Paths, store, new InstanceMetadataMigrationPipeline([]));
        var instanceService = new InstanceService(instanceRepository, fileSystem, clock);
        var archiveReader = new ModArchiveReader(fileSystem);
        var modsRepository = new FileSystemInstalledModRepository(
            fileSystem,
            instanceRepository,
            archiveReader,
            new DisabledSuffixModStateConvention(),
            store);

        var server = new ModpackTestServer();
        var handler = new FakeHttpMessageHandler(server.Respond);
        var modDbClient = new ModDbClient(
            new HttpClient(handler),
            store,
            Paths,
            clock,
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask));
        var downloads = new DownloadManager(new HttpClient(handler), fileSystem, Paths, clock);

        var gameVersionRepository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var strategy = new FakeGameInstallStrategy();
        var gameCatalog = new FakeGameVersionCatalog(ModpackTestServer.BuildGameCatalog());
        var gameInstall = new GameInstallService(gameCatalog, downloads, gameVersionRepository, strategy);

        if (gameVersionPreinstalled)
        {
            fileSystem.AddFile(
                fileSystem.Path.Combine(gameVersionRepository.GetVersionDirectory(ModpackTestServer.GameVersion), FileSystemInstalledGameVersionRepository.CompletionMarkerFileName),
                new MockFileData("1.21.3"));
        }

        var service = new ModpackImportService(
            modDbClient,
            downloads,
            modsRepository,
            instanceService,
            instanceRepository,
            gameInstall,
            gameVersionRepository,
            gameCatalog,
            fileSystem,
            clock);

        return new Harness(service, instanceRepository, modsRepository, gameVersionRepository, fileSystem, server, strategy);
    }

    private static ModpackManifest ManifestWith(params ModpackManifestMod[] mods) => new()
    {
        SchemaVersion = ModpackManifest.CurrentSchemaVersion,
        Name = "Pack de test",
        GameVersion = ModpackTestServer.GameVersion,
        Mods = mods,
    };

    private static async Task<string> WriteManifestSourceAsync(MockFileSystem fileSystem, ModpackManifest manifest, string path = "/import/pack.json")
    {
        fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(path)!);
        var stream = fileSystem.File.Create(path);
        await using (stream.ConfigureAwait(false))
        {
            await ModpackManifestSerializer.WriteAsync(stream, manifest, CancellationToken.None);
        }

        return path;
    }

    private static async Task<string> WriteArchiveSourceAsync(
        MockFileSystem fileSystem,
        ModpackManifest manifest,
        IReadOnlyDictionary<string, string>? modConfigFiles = null,
        string path = "/import/pack.zip")
    {
        fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(path)!);
        var stream = fileSystem.File.Create(path);
        await using (stream.ConfigureAwait(false))
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            var manifestEntry = archive.CreateEntry(ModpackArchiveLayout.ManifestFileName);
            var manifestStream = manifestEntry.Open();
            await using (manifestStream.ConfigureAwait(false))
            {
                await ModpackManifestSerializer.WriteAsync(manifestStream, manifest, CancellationToken.None);
            }

            foreach (var (relativePath, content) in modConfigFiles ?? new Dictionary<string, string>())
            {
                var entry = archive.CreateEntry(ModpackArchiveLayout.ModConfigEntryPrefix + relativePath);
                var entryStream = entry.Open();
                await using (entryStream.ConfigureAwait(false))
                {
                    var bytes = Encoding.UTF8.GetBytes(content);
                    await entryStream.WriteAsync(bytes);
                }
            }
        }

        return path;
    }

    private static string Sha256Hex(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    // ── Aperçu ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_ManifestOnly_ReadsNameGameVersionAndModCount()
    {
        var harness = Create();
        var manifest = ManifestWith(new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1") });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);

        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        preview.SourceFormat.ShouldBe(ModpackSourceFormat.ManifestOnly);
        preview.Manifest.Name.ShouldBe("Pack de test");
        preview.Manifest.GameVersion.ShouldBe(ModpackTestServer.GameVersion);
        preview.Manifest.Mods.Count.ShouldBe(1);
        preview.HasModConfig.ShouldBeFalse();
    }

    [Fact]
    public async Task PreviewAsync_GameVersionAlreadyInstalled_ReportsTrue()
    {
        var harness = Create(gameVersionPreinstalled: true);
        var path = await WriteManifestSourceAsync(harness.FileSystem, ManifestWith());

        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        preview.GameVersionInstalled.ShouldBeTrue();
    }

    [Fact]
    public async Task PreviewAsync_GameVersionMissing_ReportsFalse()
    {
        var harness = Create(gameVersionPreinstalled: false);
        var path = await WriteManifestSourceAsync(harness.FileSystem, ManifestWith());

        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        preview.GameVersionInstalled.ShouldBeFalse();
    }

    [Fact]
    public async Task PreviewAsync_GameVersionMissing_ReportsTheCatalogDownloadSize()
    {
        var harness = Create(gameVersionPreinstalled: false);
        var path = await WriteManifestSourceAsync(harness.FileSystem, ManifestWith());

        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        preview.GameVersionDownloadSize.ShouldBe("1 KB");
    }

    [Fact]
    public async Task PreviewAsync_GameVersionAlreadyInstalled_ReportsNoDownloadSize()
    {
        var harness = Create(gameVersionPreinstalled: true);
        var path = await WriteManifestSourceAsync(harness.FileSystem, ManifestWith());

        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        preview.GameVersionDownloadSize.ShouldBeNull();
    }

    [Fact]
    public async Task PreviewAsync_CatalogUnreachable_StillSucceedsWithoutADownloadSize()
    {
        // Confort d'affichage seulement (voir la remarque de ModpackImportService.TryResolveDownloadSizeAsync) :
        // un catalogue injoignable ne doit jamais empêcher l'aperçu du manifest.
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Noon);
        var store = new JsonFileStore(fileSystem);
        var instanceRepository = new FileSystemInstanceRepository(fileSystem, Paths, store, new InstanceMetadataMigrationPipeline([]));
        var instanceService = new InstanceService(instanceRepository, fileSystem, clock);
        var archiveReader = new ModArchiveReader(fileSystem);
        var modsRepository = new FileSystemInstalledModRepository(fileSystem, instanceRepository, archiveReader, new DisabledSuffixModStateConvention(), store);
        var server = new ModpackTestServer();
        var handler = new FakeHttpMessageHandler(server.Respond);
        var modDbClient = new ModDbClient(new HttpClient(handler), store, Paths, clock, new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask));
        var downloads = new DownloadManager(new HttpClient(handler), fileSystem, Paths, clock);
        var gameVersionRepository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var gameInstall = new GameInstallService(new UnavailableGameVersionCatalog(), downloads, gameVersionRepository, new FakeGameInstallStrategy());
        var service = new ModpackImportService(
            modDbClient, downloads, modsRepository, instanceService, instanceRepository, gameInstall,
            gameVersionRepository, new UnavailableGameVersionCatalog(), fileSystem, clock);
        var path = await WriteManifestSourceAsync(fileSystem, ManifestWith());

        var preview = await service.PreviewAsync(path, CancellationToken.None);

        preview.GameVersionInstalled.ShouldBeFalse();
        preview.GameVersionDownloadSize.ShouldBeNull();
    }

    private sealed class UnavailableGameVersionCatalog : IGameVersionCatalog
    {
        public Task<GameVersionCatalog> GetAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
            => throw new GameCatalogUnavailableException();
    }

    [Fact]
    public async Task PreviewAsync_Archive_DetectsModConfigPresence()
    {
        var harness = Create();
        var path = await WriteArchiveSourceAsync(
            harness.FileSystem,
            ManifestWith(),
            new Dictionary<string, string> { ["carrycapacity.json"] = "{}" });

        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        preview.SourceFormat.ShouldBe(ModpackSourceFormat.Archive);
        preview.HasModConfig.ShouldBeTrue();
    }

    [Fact]
    public async Task PreviewAsync_ArchiveWithoutModConfig_ReportsFalse()
    {
        var harness = Create();
        var path = await WriteArchiveSourceAsync(harness.FileSystem, ManifestWith());

        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        preview.HasModConfig.ShouldBeFalse();
    }

    [Fact]
    public async Task PreviewAsync_MissingFile_ThrowsSourceInvalid()
    {
        var harness = Create();

        await Should.ThrowAsync<ModpackSourceInvalidException>(
            () => harness.Service.PreviewAsync("/does/not/exist.json", CancellationToken.None));
    }

    [Fact]
    public async Task PreviewAsync_NeitherJsonNorZip_FallsBackToManifestReadingAndReportsInvalidJson()
    {
        // Sans signature zip ("PK"), la source est lue comme un manifest JSON autonome : des
        // octets sans rapport donnent une erreur de manifest, pas une erreur de source distincte.
        var harness = Create();
        harness.FileSystem.AddFile("/import/garbage.bin", new MockFileData(new byte[] { 1, 2, 3, 4 }));

        await Should.ThrowAsync<ModpackManifestInvalidException>(
            () => harness.Service.PreviewAsync("/import/garbage.bin", CancellationToken.None));
    }

    [Fact]
    public async Task PreviewAsync_ArchiveWithoutManifestEntry_ThrowsSourceInvalid()
    {
        var harness = Create();
        harness.FileSystem.Directory.CreateDirectory("/import");
        var stream = harness.FileSystem.File.Create("/import/empty.zip");
        await using (stream.ConfigureAwait(false))
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            archive.CreateEntry("readme.txt");
        }

        await Should.ThrowAsync<ModpackSourceInvalidException>(
            () => harness.Service.PreviewAsync("/import/empty.zip", CancellationToken.None));
    }

    // ── Création de l'instance ──────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_CreatesInstanceWithManifestNameAndGameVersion()
    {
        var harness = Create();
        var path = await WriteManifestSourceAsync(harness.FileSystem, ManifestWith());
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.Instance.Metadata.Name.ShouldBe("Pack de test");
        outcome.Instance.Metadata.GameVersion.ShouldBe(ModpackTestServer.GameVersion);
        harness.Instances.Exists(outcome.Instance.Slug).ShouldBeTrue();
    }

    [Fact]
    public async Task ImportAsync_CustomInstanceName_OverridesManifestName()
    {
        var harness = Create();
        var path = await WriteManifestSourceAsync(harness.FileSystem, ManifestWith());
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, "Nom choisi", cancellationToken: CancellationToken.None);

        outcome.Instance.Metadata.Name.ShouldBe("Nom choisi");
    }

    // ── Version de jeu ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_GameVersionMissing_InstallsItViaGameInstallService()
    {
        var harness = Create(gameVersionPreinstalled: false);
        var path = await WriteManifestSourceAsync(harness.FileSystem, ManifestWith());
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        harness.GameVersions.IsInstalled(ModpackTestServer.GameVersion).ShouldBeTrue();
        harness.Strategy.Installs.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ImportAsync_GameVersionAlreadyInstalled_DoesNotReinstall()
    {
        var harness = Create(gameVersionPreinstalled: true);
        var path = await WriteManifestSourceAsync(harness.FileSystem, ManifestWith());
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        harness.Strategy.Installs.ShouldBeEmpty();
    }

    [Fact]
    public async Task ImportAsync_GameVersionInstallFails_DeletesTheInstanceAndPropagates()
    {
        var harness = Create(gameVersionPreinstalled: false);
        harness.Strategy.Failure = new GameInstallFailedException("l'installeur a rendu l'âme");
        var path = await WriteManifestSourceAsync(harness.FileSystem, ManifestWith());
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        await Should.ThrowAsync<GameInstallFailedException>(
            () => harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None));

        (await harness.Instances.ScanAsync(CancellationToken.None)).Instances.ShouldBeEmpty();
    }

    [Fact]
    public async Task ImportAsync_ReportsGameVersionPhaseProgress()
    {
        var harness = Create(gameVersionPreinstalled: false);
        var path = await WriteManifestSourceAsync(harness.FileSystem, ManifestWith());
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);
        var phases = new List<ModpackImportPhase>();
        var progress = new SynchronousProgress<ModpackImportProgress>(report => phases.Add(report.Phase));

        await harness.Service.ImportAsync(preview, progress: progress, cancellationToken: CancellationToken.None);

        phases.ShouldContain(ModpackImportPhase.InstallingGameVersion);
    }

    // ── Résolution des mods ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_ResolvesByExactVersion_WhenNoFileIdGiven()
    {
        var harness = Create();
        var manifest = ManifestWith(new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1") });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        var report = outcome.Mods.ShouldHaveSingleItem();
        report.Status.ShouldBe(ModpackModImportStatus.Installed);
        report.InstalledVersion.ShouldBe(ModVersion.Parse("1.11.1"));
        harness.FileSystem.File
            .Exists(harness.FileSystem.Path.Combine(harness.Mods.GetModsDirectory(outcome.Instance.Slug), "configlib-1.11.1.zip"))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task ImportAsync_ResolvesByFileId_EvenIfProvided()
    {
        var harness = Create();
        var manifest = ManifestWith(new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1"), FileId = 84120 });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.Mods.ShouldHaveSingleItem().Status.ShouldBe(ModpackModImportStatus.Installed);
    }

    [Fact]
    public async Task ImportAsync_StaleFileId_FallsBackToExactVersionMatch()
    {
        var harness = Create();
        var manifest = ManifestWith(new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1"), FileId = 999999 });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.Mods.ShouldHaveSingleItem().Status.ShouldBe(ModpackModImportStatus.Installed);
    }

    [Fact]
    public async Task ImportAsync_WritesProvenanceForEachInstalledMod()
    {
        var harness = Create();
        var manifest = ManifestWith(new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1") });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        var provenance = (await harness.Mods.LoadProvenanceAsync(outcome.Instance.Slug, CancellationToken.None))["configlib-1.11.1.zip"];
        provenance.ModId.ShouldBe(1783);
        provenance.ModIdString.ShouldBe("configlib");
        provenance.ReleaseId.ShouldBe(38314);
        provenance.FileId.ShouldBe(84120);
    }

    [Fact]
    public async Task ImportAsync_MultipleMods_AreAllInstalled()
    {
        var harness = Create();
        var manifest = ManifestWith(
            new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1") },
            new ModpackManifestMod { ModId = "vsimgui", Version = ModVersion.Parse("1.3.0") });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.InstalledCount.ShouldBe(2);
        outcome.Mods.Select(mod => mod.Status).ShouldAllBe(status => status == ModpackModImportStatus.Installed);
    }

    // ── États activé/désactivé ───────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_EnabledMod_IsInstalledEnabled()
    {
        var harness = Create();
        var manifest = ManifestWith(new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1") });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        var scanned = await harness.Mods.ScanAsync(outcome.Instance.Slug, CancellationToken.None);
        scanned.ShouldHaveSingleItem().IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task ImportAsync_DisabledMod_IsInstalledDisabled()
    {
        var harness = Create();
        var manifest = ManifestWith(new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1"), Enabled = false });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        var scanned = await harness.Mods.ScanAsync(outcome.Instance.Slug, CancellationToken.None);
        var mod = scanned.ShouldHaveSingleItem();
        mod.IsEnabled.ShouldBeFalse();
        mod.FilePath.ShouldEndWith(".zip.disabled");
    }

    // ── sha256 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_MatchingSha256_Installs()
    {
        var harness = Create();
        var manifest = ManifestWith(new ModpackManifestMod
        {
            ModId = "configlib",
            Version = ModVersion.Parse("1.11.1"),
            Sha256 = Sha256Hex(ModpackTestServer.ConfigLibArchive),
        });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.Mods.ShouldHaveSingleItem().Status.ShouldBe(ModpackModImportStatus.Installed);
    }

    [Fact]
    public async Task ImportAsync_Sha256Mismatch_IsIsolatedToThatModAndDoesNotInstallIt()
    {
        var harness = Create();
        var manifest = ManifestWith(
            new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1"), Sha256 = new string('0', 64) },
            new ModpackManifestMod { ModId = "vsimgui", Version = ModVersion.Parse("1.3.0") });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.Mods.First(mod => mod.ModId == "configlib").Status.ShouldBe(ModpackModImportStatus.Sha256Mismatch);
        outcome.Mods.First(mod => mod.ModId == "vsimgui").Status.ShouldBe(ModpackModImportStatus.Installed);

        var scanned = await harness.Mods.ScanAsync(outcome.Instance.Slug, CancellationToken.None);
        scanned.Select(mod => mod.Identity).ShouldBe(["vsimgui"]);
    }

    [Fact]
    public async Task ImportAsync_NoSha256InManifest_SkipsVerification()
    {
        var harness = Create();
        var manifest = ManifestWith(new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1"), Sha256 = null });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.Mods.ShouldHaveSingleItem().Status.ShouldBe(ModpackModImportStatus.Installed);
    }

    // ── Rapport par catégorie ────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_UnknownModId_ReportsNotFound()
    {
        var harness = Create();
        var manifest = ManifestWith(new ModpackManifestMod { ModId = "does-not-exist", Version = ModVersion.Parse("1.0.0") });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.Mods.ShouldHaveSingleItem().Status.ShouldBe(ModpackModImportStatus.NotFound);
        outcome.HasIssues.ShouldBeTrue();
    }

    [Fact]
    public async Task ImportAsync_NoMatchingVersion_ReportsVersionMissingWithASuggestion()
    {
        var harness = Create();
        var manifest = ManifestWith(new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("9.9.9") });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        var report = outcome.Mods.ShouldHaveSingleItem();
        report.Status.ShouldBe(ModpackModImportStatus.VersionMissing);
        report.SuggestedVersion.ShouldBe(ModVersion.Parse("1.11.1"));
    }

    [Fact]
    public async Task ImportAsync_ModDbOffline_ReportsNetworkFailureForEachMod()
    {
        var harness = Create();
        harness.Server.ModDbOffline = true;
        var manifest = ManifestWith(new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1") });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.Mods.ShouldHaveSingleItem().Status.ShouldBe(ModpackModImportStatus.NetworkFailure);
        harness.Instances.Exists(outcome.Instance.Slug).ShouldBeTrue("un échec réseau par mod ne doit pas faire disparaître l'instance");
    }

    [Fact]
    public async Task ImportAsync_TruncatedDownloadWithSha256InManifest_IsCaughtAsAMismatch()
    {
        // Le sha256 du manifest est le seul garde-fou d'intégrité de l'import (le ModDB n'expose
        // aucune somme de contrôle) : une coupure en plein téléchargement doit donc se traduire par
        // un mismatch plutôt que par un mod silencieusement corrompu posé dans l'instance.
        var harness = Create();
        harness.Server.TruncateModDownloads = true;
        var manifest = ManifestWith(new ModpackManifestMod
        {
            ModId = "configlib",
            Version = ModVersion.Parse("1.11.1"),
            Sha256 = Sha256Hex(ModpackTestServer.ConfigLibArchive),
        });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.Mods.ShouldHaveSingleItem().Status.ShouldBe(ModpackModImportStatus.Sha256Mismatch);
    }

    [Fact]
    public async Task ImportAsync_MixedOutcomes_ReportsOneEntryPerModInManifestOrder()
    {
        var harness = Create();
        var manifest = ManifestWith(
            new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1") },
            new ModpackManifestMod { ModId = "does-not-exist", Version = ModVersion.Parse("1.0.0") },
            new ModpackManifestMod { ModId = "vsimgui", Version = ModVersion.Parse("9.9.9") });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.Mods.Select(mod => mod.ModId).ShouldBe(["configlib", "does-not-exist", "vsimgui"]);
        outcome.Mods[0].Status.ShouldBe(ModpackModImportStatus.Installed);
        outcome.Mods[1].Status.ShouldBe(ModpackModImportStatus.NotFound);
        outcome.Mods[2].Status.ShouldBe(ModpackModImportStatus.VersionMissing);
        outcome.InstalledCount.ShouldBe(1);
        harness.Instances.Exists(outcome.Instance.Slug).ShouldBeTrue();
    }

    // ── Annulation ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_CanceledDuringGameVersionInstall_DeletesTheInstance()
    {
        var harness = Create(gameVersionPreinstalled: false);
        using var cancellation = new CancellationTokenSource();
        harness.Strategy.BeforeReturning = _ => cancellation.Cancel();
        var path = await WriteManifestSourceAsync(harness.FileSystem, ManifestWith());
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        await Should.ThrowAsync<OperationCanceledException>(
            () => harness.Service.ImportAsync(preview, cancellationToken: cancellation.Token));

        (await harness.Instances.ScanAsync(CancellationToken.None)).Instances.ShouldBeEmpty();
    }

    [Fact]
    public async Task ImportAsync_CanceledDuringModsPhase_DeletesTheInstance()
    {
        var harness = Create();
        using var cancellation = new CancellationTokenSource();
        var manifest = ManifestWith(
            new ModpackManifestMod { ModId = "configlib", Version = ModVersion.Parse("1.11.1") },
            new ModpackManifestMod { ModId = "vsimgui", Version = ModVersion.Parse("1.3.0") });
        var path = await WriteManifestSourceAsync(harness.FileSystem, manifest);
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);
        var progress = new SynchronousProgress<ModpackImportProgress>(report =>
        {
            if (report.CurrentModId == "vsimgui")
            {
                cancellation.Cancel();
            }
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => harness.Service.ImportAsync(preview, progress: progress, cancellationToken: cancellation.Token));

        (await harness.Instances.ScanAsync(CancellationToken.None)).Instances.ShouldBeEmpty();
    }

    // ── ModConfig/ ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAsync_ArchiveWithModConfig_PlacesFilesUnderInstanceData()
    {
        var harness = Create();
        var path = await WriteArchiveSourceAsync(
            harness.FileSystem,
            ManifestWith(),
            new Dictionary<string, string>
            {
                ["carrycapacity.json"] = "{\"limit\":5}",
                ["nested/sub.json"] = "{}",
            });
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        var dataDirectory = harness.Instances.GetDataDirectory(outcome.Instance.Slug);
        var configPath = harness.FileSystem.Path.Combine(dataDirectory, "ModConfig", "carrycapacity.json");
        harness.FileSystem.File.Exists(configPath).ShouldBeTrue();
        harness.FileSystem.File.ReadAllText(configPath).ShouldBe("{\"limit\":5}");
        harness.FileSystem.File
            .Exists(harness.FileSystem.Path.Combine(dataDirectory, "ModConfig", "nested", "sub.json"))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task ImportAsync_ManifestOnlySource_NeverCreatesModConfig()
    {
        var harness = Create();
        var path = await WriteManifestSourceAsync(harness.FileSystem, ManifestWith());
        var preview = await harness.Service.PreviewAsync(path, CancellationToken.None);

        var outcome = await harness.Service.ImportAsync(preview, cancellationToken: CancellationToken.None);

        var dataDirectory = harness.Instances.GetDataDirectory(outcome.Instance.Slug);
        harness.FileSystem.Directory.Exists(harness.FileSystem.Path.Combine(dataDirectory, "ModConfig")).ShouldBeFalse();
    }

    // ── Constructeur ────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var harness = Create();
        var fileSystem = harness.FileSystem;
        var store = new JsonFileStore(fileSystem);
        var instanceRepository = harness.Instances;
        var instanceService = new InstanceService(instanceRepository, fileSystem, new FakeClock(Noon));
        var gameCatalog = new FakeGameVersionCatalog(ModpackTestServer.BuildGameCatalog());
        var gameInstall = new GameInstallService(
            gameCatalog,
            new DownloadManager(new HttpClient(new FakeHttpMessageHandler(harness.Server.Respond)), fileSystem, Paths, new FakeClock(Noon)),
            harness.GameVersions,
            harness.Strategy);
        var modDb = new ModDbClient(
            new HttpClient(new FakeHttpMessageHandler(harness.Server.Respond)),
            store,
            Paths,
            new FakeClock(Noon),
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask));
        var downloads = new DownloadManager(new HttpClient(new FakeHttpMessageHandler(harness.Server.Respond)), fileSystem, Paths, new FakeClock(Noon));
        var clock = new FakeClock(Noon);

        Should.Throw<ArgumentNullException>(() => new ModpackImportService(null!, downloads, harness.Mods, instanceService, instanceRepository, gameInstall, harness.GameVersions, gameCatalog, fileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new ModpackImportService(modDb, null!, harness.Mods, instanceService, instanceRepository, gameInstall, harness.GameVersions, gameCatalog, fileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new ModpackImportService(modDb, downloads, null!, instanceService, instanceRepository, gameInstall, harness.GameVersions, gameCatalog, fileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new ModpackImportService(modDb, downloads, harness.Mods, null!, instanceRepository, gameInstall, harness.GameVersions, gameCatalog, fileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new ModpackImportService(modDb, downloads, harness.Mods, instanceService, null!, gameInstall, harness.GameVersions, gameCatalog, fileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new ModpackImportService(modDb, downloads, harness.Mods, instanceService, instanceRepository, null!, harness.GameVersions, gameCatalog, fileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new ModpackImportService(modDb, downloads, harness.Mods, instanceService, instanceRepository, gameInstall, null!, gameCatalog, fileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new ModpackImportService(modDb, downloads, harness.Mods, instanceService, instanceRepository, gameInstall, harness.GameVersions, null!, fileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new ModpackImportService(modDb, downloads, harness.Mods, instanceService, instanceRepository, gameInstall, harness.GameVersions, gameCatalog, null!, clock));
        Should.Throw<ArgumentNullException>(() => new ModpackImportService(modDb, downloads, harness.Mods, instanceService, instanceRepository, gameInstall, harness.GameVersions, gameCatalog, fileSystem, null!));
    }
}