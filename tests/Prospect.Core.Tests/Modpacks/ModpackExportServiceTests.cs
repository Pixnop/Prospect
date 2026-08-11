using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Modpacks;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.ModDb;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Modpacks;

public sealed class ModpackExportServiceTests
{
    private const string Slug = "homestead-121";

    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(ModpackExportService Service, IInstalledModRepository Mods, IInstanceRepository Instances, MockFileSystem FileSystem);

    private static Harness Create(string gameVersion = "1.21.3")
    {
        var fileSystem = new MockFileSystem();
        var store = new JsonFileStore(fileSystem);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, store, new InstanceMetadataMigrationPipeline([]));
        var archiveReader = new ModArchiveReader(fileSystem);
        var mods = new FileSystemInstalledModRepository(
            fileSystem,
            instances,
            archiveReader,
            new DisabledSuffixModStateConvention(),
            store);

        SeedInstance(fileSystem, gameVersion);

        return new Harness(new ModpackExportService(instances, mods, fileSystem), mods, instances, fileSystem);
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

    private static void AddMod(Harness harness, string fileName, byte[] archive, bool enabled = true)
    {
        var modsDirectory = harness.Mods.GetModsDirectory(Slug);
        var name = enabled ? fileName : fileName + ".disabled";
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(modsDirectory, name), new MockFileData(archive));
    }

    private static void AddModConfigFile(Harness harness, string relativePath, string content)
    {
        var dataDirectory = harness.Instances.GetDataDirectory(Slug);
        var fullPath = harness.FileSystem.Path.Combine(dataDirectory, "ModConfig", relativePath);
        harness.FileSystem.AddFile(fullPath, new MockFileData(content));
    }

    private static string Sha256Hex(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    // ── Manifest seul ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_ManifestOnly_WritesTheInstanceHeader()
    {
        var harness = Create();
        var destination = "/out/pack.json";

        var result = await harness.Service.ExportAsync(Slug, destination, new ModpackExportOptions(ModpackExportFormat.ManifestOnly), CancellationToken.None);

        result.DestinationPath.ShouldBe(destination);
        harness.FileSystem.File.Exists(destination).ShouldBeTrue();

        var manifest = await ReadManifestOnlyAsync(harness, destination);
        manifest.Name.ShouldBe("Homestead 1.21");
        manifest.GameVersion.ShouldBe(GameVersion.Parse("1.21.3"));
        manifest.SchemaVersion.ShouldBe(ModpackManifest.CurrentSchemaVersion);
    }

    [Fact]
    public async Task ExportAsync_ManifestOnly_ListsEachIdentifiedModWithItsSha256()
    {
        var harness = Create();
        var archive = ModInfoSamples.BuildArchive(ModInfoSamples.ConfigLib);
        AddMod(harness, "configlib.zip", archive);
        var destination = "/out/pack.json";

        var result = await harness.Service.ExportAsync(Slug, destination, new ModpackExportOptions(ModpackExportFormat.ManifestOnly), CancellationToken.None);

        result.ModsExported.ShouldBe(1);
        result.SkippedMods.ShouldBeEmpty();

        var manifest = await ReadManifestOnlyAsync(harness, destination);
        var mod = manifest.Mods.ShouldHaveSingleItem();
        mod.ModId.ShouldBe("configlib");
        mod.Version.ShouldBe(ModVersion.Parse("1.12.0"));
        mod.Sha256.ShouldBe(Sha256Hex(archive));
        mod.IsEnabled.ShouldBeTrue();
        mod.Enabled.ShouldBeNull();
    }

    [Fact]
    public async Task ExportAsync_DisabledMod_TravelsWithItsStateExplicit()
    {
        var harness = Create();
        AddMod(harness, "configlib.zip", ModInfoSamples.BuildArchive(ModInfoSamples.ConfigLib), enabled: false);
        var destination = "/out/pack.json";

        await harness.Service.ExportAsync(Slug, destination, new ModpackExportOptions(ModpackExportFormat.ManifestOnly), CancellationToken.None);

        var manifest = await ReadManifestOnlyAsync(harness, destination);
        var mod = manifest.Mods.ShouldHaveSingleItem();
        mod.IsEnabled.ShouldBeFalse();
        mod.Enabled.ShouldBe(false);
    }

    [Fact]
    public async Task ExportAsync_ProvenanceKnown_CarriesTheFileIdAsAShortcut()
    {
        var harness = Create();
        AddMod(harness, "configlib-1.12.0.zip", ModInfoSamples.BuildArchive(ModInfoSamples.ConfigLib));
        await harness.Mods.SaveProvenanceAsync(
            Slug,
            new ModProvenance
            {
                FileName = "configlib-1.12.0.zip",
                ModId = 1783,
                ModIdString = "configlib",
                ReleaseId = 38314,
                FileId = 84120,
                Version = ModVersion.Parse("1.12.0"),
                InstalledUtc = Noon,
            },
            CancellationToken.None);
        var destination = "/out/pack.json";

        await harness.Service.ExportAsync(Slug, destination, new ModpackExportOptions(ModpackExportFormat.ManifestOnly), CancellationToken.None);

        var manifest = await ReadManifestOnlyAsync(harness, destination);
        manifest.Mods.ShouldHaveSingleItem().FileId.ShouldBe(84120);
    }

    // ── Mods laissés de côté ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_ModWithoutReadableModInfo_IsListedAsSkippedRatherThanSilentlyDropped()
    {
        var harness = Create();
        AddMod(harness, "mystery.zip", ModInfoSamples.BuildArchive(null));
        var destination = "/out/pack.json";

        var result = await harness.Service.ExportAsync(Slug, destination, new ModpackExportOptions(ModpackExportFormat.ManifestOnly), CancellationToken.None);

        result.ModsExported.ShouldBe(0);
        result.HasSkippedMods.ShouldBeTrue();
        var skipped = result.SkippedMods.ShouldHaveSingleItem();
        skipped.FileName.ShouldBe("mystery.zip");
        skipped.Reason.ShouldBe(ModpackExportSkipReason.UnreadableModInfo);

        var manifest = await ReadManifestOnlyAsync(harness, destination);
        manifest.Mods.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExportAsync_ModWithUnparseableVersion_IsListedAsSkipped()
    {
        var harness = Create();
        const string brokenVersionModInfo = """
        { "name": "Broken Version Mod", "modid": "brokenversion", "version": "not-a-version" }
        """;
        AddMod(harness, "brokenversion.zip", ModInfoSamples.BuildArchive(brokenVersionModInfo));
        var destination = "/out/pack.json";

        var result = await harness.Service.ExportAsync(Slug, destination, new ModpackExportOptions(ModpackExportFormat.ManifestOnly), CancellationToken.None);

        result.SkippedMods.ShouldHaveSingleItem().Reason.ShouldBe(ModpackExportSkipReason.MissingVersion);
    }

    // ── Archive ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_Archive_ContainsTheManifestEntry()
    {
        var harness = Create();
        AddMod(harness, "configlib.zip", ModInfoSamples.BuildArchive(ModInfoSamples.ConfigLib));
        var destination = "/out/pack.zip";

        await harness.Service.ExportAsync(Slug, destination, new ModpackExportOptions(ModpackExportFormat.Archive), CancellationToken.None);

        using var zipStream = harness.FileSystem.File.OpenRead(destination);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var manifestEntry = zip.GetEntry(ModpackArchiveLayout.ManifestFileName);
        manifestEntry.ShouldNotBeNull();

        using var manifestStream = manifestEntry.Open();
        var manifest = await ModpackManifestSerializer.ReadAsync(manifestStream, CancellationToken.None);
        manifest.Mods.ShouldHaveSingleItem().ModId.ShouldBe("configlib");
    }

    [Fact]
    public async Task ExportAsync_ArchiveWithModConfigRequested_EmbedsItsFilesUnderThePrefix()
    {
        var harness = Create();
        AddModConfigFile(harness, "carrycapacity.json", "{\"limit\":5}");
        AddModConfigFile(harness, "nested/sub.json", "{}");
        var destination = "/out/pack.zip";

        await harness.Service.ExportAsync(
            Slug,
            destination,
            new ModpackExportOptions(ModpackExportFormat.Archive, IncludeModConfig: true),
            CancellationToken.None);

        using var zipStream = harness.FileSystem.File.OpenRead(destination);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        zip.GetEntry("ModConfig/carrycapacity.json").ShouldNotBeNull();
        zip.GetEntry("ModConfig/nested/sub.json").ShouldNotBeNull();

        using var entryStream = zip.GetEntry("ModConfig/carrycapacity.json")!.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        (await reader.ReadToEndAsync()).ShouldBe("{\"limit\":5}");
    }

    [Fact]
    public async Task ExportAsync_ArchiveWithoutModConfigRequested_OmitsIt()
    {
        var harness = Create();
        AddModConfigFile(harness, "carrycapacity.json", "{}");
        var destination = "/out/pack.zip";

        await harness.Service.ExportAsync(
            Slug,
            destination,
            new ModpackExportOptions(ModpackExportFormat.Archive, IncludeModConfig: false),
            CancellationToken.None);

        using var zipStream = harness.FileSystem.File.OpenRead(destination);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        zip.Entries.ShouldAllBe(entry => !entry.FullName.StartsWith("ModConfig/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportAsync_ArchiveWithoutModConfigFolderOnDisk_StillSucceeds()
    {
        var harness = Create();
        var destination = "/out/pack.zip";

        await Should.NotThrowAsync(() => harness.Service.ExportAsync(
            Slug,
            destination,
            new ModpackExportOptions(ModpackExportFormat.Archive, IncludeModConfig: true),
            CancellationToken.None));
    }

    // ── Instance introuvable ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_UnknownInstance_ThrowsAndWritesNothing()
    {
        var harness = Create();
        var destination = "/out/pack.json";

        await Should.ThrowAsync<InstanceNotFoundException>(
            () => harness.Service.ExportAsync("no-such-instance", destination, new ModpackExportOptions(ModpackExportFormat.ManifestOnly), CancellationToken.None));

        harness.FileSystem.File.Exists(destination).ShouldBeFalse();
    }

    private static async Task<ModpackManifest> ReadManifestOnlyAsync(Harness harness, string destination)
    {
        using var stream = harness.FileSystem.File.OpenRead(destination);

        return await ModpackManifestSerializer.ReadAsync(stream, CancellationToken.None);
    }
}