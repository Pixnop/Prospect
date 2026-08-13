using System.IO.Abstractions.TestingHelpers;

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
using Prospect.Core.Tests.ModDb;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Modpacks;

/// <summary>
/// Le test d'intégration naturel du Core (docs/architecture.md, « 5. Modpacks ») : une instance
/// peuplée de mods (un actif, un désactivé) et d'un dossier <c>ModConfig/</c>, exportée en archive,
/// puis importée dans une instance neuve. L'instance résultante doit être équivalente à la source :
/// même version de jeu, mêmes mods à la même version et au même état, même contenu de
/// <c>ModConfig/</c>. Aucun mock de haut niveau ici : les mêmes services concrets que la production
/// (<see cref="ModpackExportService"/>, <see cref="ModpackImportService"/>,
/// <see cref="FileSystemInstalledModRepository"/>, <see cref="GameInstallService"/>...) tournent
/// contre un <see cref="MockFileSystem"/> et un unique répondeur HTTP factice.
/// </summary>
public sealed class ModpackRoundTripTests
{
    private const string SourceSlug = "homestead-source";

    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record World(
        ModpackExportService ExportService,
        ModpackImportService ImportService,
        IInstanceRepository Instances,
        IInstalledModRepository Mods,
        IInstalledGameVersionRepository GameVersions,
        MockFileSystem FileSystem);

    private static World CreateWorld()
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
        var strategy = new FakeGameInstallStrategy(fileSystem);
        var gameCatalog = new FakeGameVersionCatalog(ModpackTestServer.BuildGameCatalog());
        var gameInstall = new GameInstallService(gameCatalog, downloads, gameVersionRepository, strategy, fileSystem, NullAppLog.Instance);

        var exportService = new ModpackExportService(instanceRepository, modsRepository, fileSystem);
        var importService = new ModpackImportService(
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

        SeedSourceInstance(fileSystem, instanceRepository, modsRepository);

        return new World(exportService, importService, instanceRepository, modsRepository, gameVersionRepository, fileSystem);
    }

    private static void SeedSourceInstance(MockFileSystem fileSystem, FileSystemInstanceRepository instances, FileSystemInstalledModRepository mods)
    {
        var metadata = new InstanceMetadata
        {
            SchemaVersion = InstanceMetadata.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            Name = "Homestead source",
            GameVersion = ModpackTestServer.GameVersion,
            CreatedUtc = Noon,
        };

        fileSystem.AddFile(
            fileSystem.Path.Combine(Paths.InstancesDirectory, SourceSlug, "instance.json"),
            new MockFileData(System.Text.Json.JsonSerializer.Serialize(metadata, InstanceJsonContext.Default.InstanceMetadata)));

        var modsDirectory = mods.GetModsDirectory(SourceSlug);
        fileSystem.AddFile(
            // Le modinfo.json embarqué dans ConfigLibArchive déclare la version 1.12.0 (échantillon
            // réel, docs/research/moddb-api.md) : le nom de fichier local le reflète ici, même s'il
            // n'a aucune incidence sur la résolution à l'import (qui ne se fie qu'au manifest).
            fileSystem.Path.Combine(modsDirectory, "configlib-1.12.0.zip"),
            new MockFileData(ModpackTestServer.ConfigLibArchive));
        fileSystem.AddFile(
            // Désactivé (suffixe .disabled) : son état doit survivre au round-trip.
            fileSystem.Path.Combine(modsDirectory, "vsimgui-1.3.0.zip.disabled"),
            new MockFileData(ModpackTestServer.VsImGuiArchive));

        var modConfigPath = fileSystem.Path.Combine(instances.GetDataDirectory(SourceSlug), "ModConfig", "configlib.json");
        fileSystem.AddFile(modConfigPath, new MockFileData("{\"limit\":5}"));
    }

    [Fact]
    public async Task ExportThenImport_ProducesAnEquivalentInstance()
    {
        var world = CreateWorld();
        const string archivePath = "/out/homestead.zip";

        var exportResult = await world.ExportService.ExportAsync(
            SourceSlug,
            archivePath,
            new ModpackExportOptions(ModpackExportFormat.Archive, IncludeModConfig: true),
            CancellationToken.None);

        exportResult.ModsExported.ShouldBe(2);
        exportResult.SkippedMods.ShouldBeEmpty();

        var preview = await world.ImportService.PreviewAsync(archivePath, CancellationToken.None);
        preview.Manifest.Name.ShouldBe("Homestead source");
        preview.Manifest.GameVersion.ShouldBe(ModpackTestServer.GameVersion);
        preview.Manifest.Mods.Count.ShouldBe(2);
        preview.HasModConfig.ShouldBeTrue();
        // La version de jeu n'a jamais été installée dans ce monde neuf : l'import doit donc
        // l'installer lui-même via GameInstallService, exactement comme le prévoit le contrat.
        preview.GameVersionInstalled.ShouldBeFalse();

        var outcome = await world.ImportService.ImportAsync(preview, cancellationToken: CancellationToken.None);

        // ── Instance équivalente : version de jeu ──────────────────────────────
        outcome.Instance.Metadata.Name.ShouldBe("Homestead source");
        outcome.Instance.Metadata.GameVersion.ShouldBe(ModpackTestServer.GameVersion);
        world.GameVersions.IsInstalled(ModpackTestServer.GameVersion).ShouldBeTrue();

        // ── Instance équivalente : rapport et mods ─────────────────────────────
        outcome.HasIssues.ShouldBeFalse();
        outcome.InstalledCount.ShouldBe(2);

        var importedMods = await world.Mods.ScanAsync(outcome.Instance.Slug, CancellationToken.None);
        var sourceMods = await world.Mods.ScanAsync(SourceSlug, CancellationToken.None);

        importedMods.Select(mod => mod.Identity).OrderBy(id => id, StringComparer.Ordinal)
            .ShouldBe(sourceMods.Select(mod => mod.Identity).OrderBy(id => id, StringComparer.Ordinal));

        var importedConfigLib = importedMods.Single(mod => mod.Identity == "configlib");
        var sourceConfigLib = sourceMods.Single(mod => mod.Identity == "configlib");
        importedConfigLib.Version.ShouldBe(sourceConfigLib.Version);
        importedConfigLib.IsEnabled.ShouldBe(sourceConfigLib.IsEnabled);
        importedConfigLib.IsEnabled.ShouldBeTrue();

        var importedVsImGui = importedMods.Single(mod => mod.Identity == "vsimgui");
        var sourceVsImGui = sourceMods.Single(mod => mod.Identity == "vsimgui");
        importedVsImGui.Version.ShouldBe(sourceVsImGui.Version);
        importedVsImGui.IsEnabled.ShouldBe(sourceVsImGui.IsEnabled);
        importedVsImGui.IsEnabled.ShouldBeFalse("le mod était désactivé dans l'instance source, son état doit voyager");

        // ── Instance équivalente : ModConfig/ ──────────────────────────────────
        var importedConfigPath = world.FileSystem.Path.Combine(
            world.Instances.GetDataDirectory(outcome.Instance.Slug), "ModConfig", "configlib.json");
        world.FileSystem.File.Exists(importedConfigPath).ShouldBeTrue();
        world.FileSystem.File.ReadAllText(importedConfigPath).ShouldBe("{\"limit\":5}");
    }

    [Fact]
    public async Task ExportThenImport_ManifestOnly_RecreatesEveryModWithoutModConfig()
    {
        var world = CreateWorld();
        const string manifestPath = "/out/homestead.json";

        await world.ExportService.ExportAsync(
            SourceSlug,
            manifestPath,
            new ModpackExportOptions(ModpackExportFormat.ManifestOnly),
            CancellationToken.None);

        var preview = await world.ImportService.PreviewAsync(manifestPath, CancellationToken.None);
        preview.SourceFormat.ShouldBe(ModpackSourceFormat.ManifestOnly);

        var outcome = await world.ImportService.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.InstalledCount.ShouldBe(2);
        var dataDirectory = world.Instances.GetDataDirectory(outcome.Instance.Slug);
        world.FileSystem.Directory.Exists(world.FileSystem.Path.Combine(dataDirectory, "ModConfig")).ShouldBeFalse();
    }

    [Fact]
    public async Task ExportThenImport_Sha256FromExportIsVerifiedAndAccepted()
    {
        var world = CreateWorld();
        const string archivePath = "/out/homestead.zip";
        await world.ExportService.ExportAsync(
            SourceSlug,
            archivePath,
            new ModpackExportOptions(ModpackExportFormat.Archive, IncludeModConfig: false),
            CancellationToken.None);

        var preview = await world.ImportService.PreviewAsync(archivePath, CancellationToken.None);
        preview.Manifest.Mods.ShouldAllBe(mod => mod.Sha256 != null);

        var outcome = await world.ImportService.ImportAsync(preview, cancellationToken: CancellationToken.None);

        outcome.Mods.ShouldAllBe(mod => mod.Status == ModpackModImportStatus.Installed);
    }
}