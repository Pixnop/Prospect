using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

using Prospect.Core.Common;
using Prospect.Core.Diagnostics;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Diagnostics;

/// <summary>
/// <see cref="GameLogInsightsService"/> : le croisement du journal du dernier lancement avec l'état
/// actuel du dossier <c>Mods/</c>. C'est ici que « le mod X vise le domaine Y » devient
/// « fonctionne avec Y » (Y est installé) ou « attend du contenu de Y » (Y n'est pas là).
/// </summary>
public sealed class GameLogInsightsServiceTests
{
    private const string Slug = "survie";
    private const string LogPath = "/data/prospect/logs/instance-survie.log";

    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(GameLogInsightsService Service, MockFileSystem FileSystem, IInstalledModRepository Mods);

    private static Harness Create()
    {
        var fileSystem = new MockFileSystem();
        var store = new JsonFileStore(fileSystem);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, store, new InstanceMetadataMigrationPipeline([]));
        var mods = new FileSystemInstalledModRepository(fileSystem, instances, new ModArchiveReader(fileSystem), new DisabledSuffixModStateConvention(), store);

        SeedInstance(fileSystem);

        var service = new GameLogInsightsService(fileSystem, mods, new ModIntegrationScanner(fileSystem), new FakeClock(Noon));

        return new Harness(service, fileSystem, mods);
    }

    private static void SeedInstance(MockFileSystem fileSystem)
    {
        var metadata = new InstanceMetadata
        {
            SchemaVersion = InstanceMetadata.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            Name = "Survie",
            GameVersion = GameVersion.Parse("1.22.6"),
            CreatedUtc = Noon,
        };

        fileSystem.AddFile(
            fileSystem.Path.Combine(Paths.InstancesDirectory, Slug, "instance.json"),
            new MockFileData(JsonSerializer.Serialize(metadata, InstanceJsonContext.Default.InstanceMetadata)));
        fileSystem.AddDirectory(fileSystem.Path.Combine(Paths.InstancesDirectory, Slug, "data", "Mods"));
    }

    private static void SeedMod(
        Harness harness,
        string modId,
        bool enabled = true,
        IReadOnlyDictionary<string, string>? extraEntries = null)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["modinfo.json"] = $$"""{ "type": "content", "modid": "{{modId}}", "name": "{{modId}}", "version": "1.0.0" }""",
        };

        foreach (var (name, content) in extraEntries ?? new Dictionary<string, string>())
        {
            entries[name] = content;
        }

        var fileName = enabled ? $"{modId}-1.0.0.zip" : $"{modId}-1.0.0.zip.disabled";
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(harness.Mods.GetModsDirectory(Slug), fileName),
            new MockFileData(BuildArchive(entries)));
    }

    private static byte[] BuildArchive(IReadOnlyDictionary<string, string> entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return buffer.ToArray();
    }

    private static void SeedLog(Harness harness, string content)
        => harness.FileSystem.AddFile(LogPath, new MockFileData(content.ReplaceLineEndings("\n")));

    [Fact]
    public async Task AnalyzeAsync_InstanceNeverLaunched_SaysSoWithoutFailing()
    {
        var harness = Create();
        SeedMod(harness, "carryon");

        var insights = await harness.Service.AnalyzeAsync(Slug, LogPath);

        insights.HasLog.ShouldBeFalse();
        insights.Mods.ShouldBeEmpty();
        insights.AnalyzedUtc.ShouldBe(Noon);
    }

    [Fact]
    public async Task AnalyzeAsync_LogWithAModError_ReportsItForThatInstalledMod()
    {
        var harness = Create();
        SeedMod(harness, "hearthside");
        SeedLog(harness, """
        [2026-08-13T19:08:20.0000000+00:00] Lancement de « Survie » (1.22.6)
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: hearthside, game
        13.8.2026 21:08:23 [Client Error] [hearthside] Could not resolve some dependencies:
        13.8.2026 21:08:24 [Client Warning] [hearthside] a shape is missing, using a cube instead
        """);

        var insights = await harness.Service.AnalyzeAsync(Slug, LogPath);

        insights.HasLog.ShouldBeTrue();

        var installed = (await harness.Mods.ScanAsync(Slug)).Single();
        var verdict = insights.FindMod(installed).ShouldNotBeNull();
        verdict.ErrorCount.ShouldBe(1);
        verdict.WarningCount.ShouldBe(1);
        verdict.Samples.Count.ShouldBe(2);
    }

    /// <summary>
    /// Le jeu nomme par son ARCHIVE un mod dont il n'a pas pu lire le <c>modinfo.json</c>, et c'est
    /// précisément ce mod-là qui n'a pas d'identité à faire correspondre : le rapprochement doit
    /// donc aussi savoir se faire par nom de fichier.
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_ModNamedByItsArchive_IsMatchedToTheFileOnDisk()
    {
        var harness = Create();
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(harness.Mods.GetModsDirectory(Slug), "ruinedcompass-0.4.2.zip"),
            new MockFileData(BuildArchive(new Dictionary<string, string> { ["readme.txt"] = "pas de modinfo" })));
        SeedLog(harness, """
        13.8.2026 21:08:23 [Client Error] [ruinedcompass-0.4.2.zip] An exception was thrown trying to to load the ModInfo:
        """);

        var insights = await harness.Service.AnalyzeAsync(Slug, LogPath);

        var installed = (await harness.Mods.ScanAsync(Slug)).Single();
        insights.FindMod(installed).ShouldNotBeNull().ErrorCount.ShouldBe(1);
    }

    [Fact]
    public async Task AnalyzeAsync_ModTargetingAnInstalledMod_ReportsAResolvedIntegration()
    {
        var harness = Create();
        SeedMod(harness, "carryon", extraEntries: new Dictionary<string, string>
        {
            ["assets/carryon/patches/crates.json"] = """[{ "file": "bettercrates:blocktypes/a", "op": "add", "path": "/x", "value": 1 }]""",
        });
        SeedMod(harness, "bettercrates");
        SeedLog(harness, "13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: carryon, bettercrates, game");

        var insights = await harness.Service.AnalyzeAsync(Slug, LogPath);

        var integration = insights.Integrations.ShouldHaveSingleItem();
        integration.SourceModId.ShouldBe("carryon");
        integration.TargetModId.ShouldBe("bettercrates");
        integration.Nature.ShouldBe(ModIntegrationNature.Resolved);
    }

    [Fact]
    public async Task AnalyzeAsync_ConditionalTargetThatIsNotInstalled_IsOnlyOptional()
    {
        var harness = Create();
        SeedMod(harness, "carryon", extraEntries: new Dictionary<string, string>
        {
            ["assets/carryon/patches/crates.json"] = """
            [{ "file": "bettercrates:blocktypes/a", "op": "add", "path": "/x", "value": 1, "dependsOn": [{ "modid": "bettercrates" }] }]
            """,
        });

        var insights = await harness.Service.AnalyzeAsync(Slug, LogPath);

        insights.Integrations.ShouldHaveSingleItem().Nature.ShouldBe(ModIntegrationNature.Optional);
    }

    [Fact]
    public async Task AnalyzeAsync_UnconditionalTargetThatIsNotInstalled_IsAMissingReference()
    {
        var harness = Create();
        SeedMod(harness, "carryon", extraEntries: new Dictionary<string, string>
        {
            ["assets/carryon/patches/crates.json"] = """[{ "file": "bettercrates:blocktypes/a", "op": "add", "path": "/x", "value": 1 }]""",
        });

        var insights = await harness.Service.AnalyzeAsync(Slug, LogPath);

        insights.Integrations.ShouldHaveSingleItem().Nature.ShouldBe(ModIntegrationNature.Missing);
    }

    /// <summary>
    /// Le zip dit « je patche ce mod », le jeu dit « ce fichier n'existe pas » : c'est le jeu qui a
    /// raison, une intégration que le moteur a refusée ne fonctionne pas.
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_TargetInstalledButThePatchFailed_KeepsTheGamesVerdict()
    {
        var harness = Create();
        SeedMod(harness, "carryon", extraEntries: new Dictionary<string, string>
        {
            ["assets/carryon/patches/crates.json"] = """[{ "file": "bettercrates:blocktypes/a", "op": "add", "path": "/x", "value": 1 }]""",
        });
        SeedMod(harness, "bettercrates");
        SeedLog(harness, """
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: carryon, bettercrates, game
        13.8.2026 21:08:24 [Client Error] Patch 0 in carryon:patches/crates.json: File bettercrates:blocktypes/a.json not found
        """);

        var insights = await harness.Service.AnalyzeAsync(Slug, LogPath);

        insights.Integrations.ShouldHaveSingleItem().Nature.ShouldBe(ModIntegrationNature.Missing);
    }

    [Fact]
    public async Task AnalyzeAsync_DisabledMod_IsNeitherScannedNorCountedAsPresent()
    {
        var harness = Create();
        SeedMod(harness, "carryon", extraEntries: new Dictionary<string, string>
        {
            ["assets/carryon/patches/crates.json"] = """[{ "file": "bettercrates:blocktypes/a", "op": "add", "path": "/x", "value": 1 }]""",
        });
        SeedMod(harness, "bettercrates", enabled: false);

        var insights = await harness.Service.AnalyzeAsync(Slug, LogPath);

        insights.Integrations.ShouldHaveSingleItem().Nature.ShouldBe(ModIntegrationNature.Missing);
    }

    [Fact]
    public async Task AnalyzeAsync_ModThatIsFine_HasNothingToShow()
    {
        var harness = Create();
        SeedMod(harness, "carryon");
        SeedLog(harness, """
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: carryon, game
        13.8.2026 21:08:24 [Client Notification] Loaded 5300 unique items
        """);

        var insights = await harness.Service.AnalyzeAsync(Slug, LogPath);

        var installed = (await harness.Mods.ScanAsync(Slug)).Single();
        insights.FindMod(installed).ShouldBeNull();
        insights.FindIntegrations(installed).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_IntegrationsOfOneMod_AreFoundFromTheInstalledMod()
    {
        var harness = Create();
        SeedMod(harness, "carryon", extraEntries: new Dictionary<string, string>
        {
            ["assets/carryon/patches/crates.json"] = """[{ "file": "bettercrates:blocktypes/a", "op": "add", "path": "/x", "value": 1 }]""",
        });
        SeedMod(harness, "bettercrates");

        var insights = await harness.Service.AnalyzeAsync(Slug, LogPath);

        var carryon = (await harness.Mods.ScanAsync(Slug)).Single(mod => mod.Identity == "carryon");
        var bettercrates = (await harness.Mods.ScanAsync(Slug)).Single(mod => mod.Identity == "bettercrates");

        insights.FindIntegrations(carryon).ShouldHaveSingleItem().TargetModId.ShouldBe("bettercrates");
        insights.FindIntegrations(bettercrates).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_NoInstanceMods_StillReadsTheLog()
    {
        var harness = Create();
        SeedLog(harness, """
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: hearthside, game
        13.8.2026 21:08:23 [Client Error] [hearthside] Could not resolve some dependencies:
        """);

        var insights = await harness.Service.AnalyzeAsync(Slug, LogPath);

        insights.Mods.ShouldHaveSingleItem().ModId.ShouldBe("hearthside");
        insights.Integrations.ShouldBeEmpty();
    }

    /// <summary>
    /// Le jeu peut tenir son journal ouvert pendant qu'on le lit : un refus de lecture rend un
    /// rapport vide, pas une exception qui remonterait jusqu'à la liste des mods.
    /// </summary>
    [Fact]
    public async Task AnalyzeAsync_JournalThatRefusesToBeRead_ReportsNothingWithoutThrowing()
    {
        var harness = Create();
        SeedMod(harness, "hearthside");
        harness.FileSystem.AddFile(LogPath, new MockFileData("13.8.2026 21:08:23 [Client Error] [hearthside] boum")
        {
            AllowedFileShare = FileShare.None,
        });

        var insights = await harness.Service.AnalyzeAsync(Slug, LogPath);

        insights.HasLog.ShouldBeTrue();
        insights.Mods.ShouldBeEmpty();
    }

    [Fact]
    public void None_IsTheNeutralResult()
    {
        InstanceLogInsights.None.HasLog.ShouldBeFalse();
        InstanceLogInsights.None.Mods.ShouldBeEmpty();
        InstanceLogInsights.None.Integrations.ShouldBeEmpty();
    }
}