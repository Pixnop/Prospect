using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Migration;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Instances;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Migration;

/// <summary>
/// Bout-en-bout sur <see cref="MockFileSystem"/>, comme <c>ModpackImportServiceTests</c> : le
/// service compose des dépendances réelles (InstanceService, les deux repositories) plutôt que des
/// doubles, pour vérifier ce qui atterrit réellement sur le système de fichiers factice.
/// </summary>
public sealed class VslAdoptionServiceTests
{
    private static readonly AppPaths ProspectPaths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        VslAdoptionService Service,
        IInstanceRepository Instances,
        IInstalledGameVersionRepository GameVersions,
        MockFileSystem FileSystem);

    private static Harness Create()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var store = new JsonFileStore(fileSystem);
        var instanceRepository = new FileSystemInstanceRepository(fileSystem, ProspectPaths, store, new InstanceMetadataMigrationPipeline([]));
        var instanceService = new InstanceService(instanceRepository, fileSystem, clock);
        var gameVersionRepository = new FileSystemInstalledGameVersionRepository(fileSystem, ProspectPaths);

        var service = new VslAdoptionService(instanceService, instanceRepository, gameVersionRepository, store, fileSystem, clock);

        return new Harness(service, instanceRepository, gameVersionRepository, fileSystem);
    }

    private static VslInstallation Installation(
        string name = "Survie médiévale",
        string path = "/vsl/installations/survie",
        string version = "1.20.4",
        string startParams = "",
        string envVars = "",
        bool mesaGlThread = false,
        long lastTimePlayedMs = -1,
        long totalTimePlayedMs = 0)
        => new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Path = path,
            Version = version,
            StartParams = startParams,
            EnvVars = envVars,
            MesaGlThread = mesaGlThread,
            LastTimePlayedMs = lastTimePlayedMs,
            TotalTimePlayedMs = totalTimePlayedMs,
        };

    private static void SeedInstallationFolder(MockFileSystem fileSystem, string path)
    {
        fileSystem.AddFile(fileSystem.Path.Combine(path, "Mods", "carrycapacity.zip"), new MockFileData("mod-content"));
        fileSystem.AddFile(fileSystem.Path.Combine(path, "Saves", "world.vcdbs"), new MockFileData("save-content"));
        fileSystem.AddFile(fileSystem.Path.Combine(path, "clientsettings.json"), new MockFileData("{}"));
    }

    // ── Constructeur ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var harness = Create();
        var clock = new FakeClock(Now);
        var store = new JsonFileStore(harness.FileSystem);
        var instanceService = new InstanceService(harness.Instances, harness.FileSystem, clock);

        Should.Throw<ArgumentNullException>(() => new VslAdoptionService(null!, harness.Instances, harness.GameVersions, store, harness.FileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new VslAdoptionService(instanceService, null!, harness.GameVersions, store, harness.FileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new VslAdoptionService(instanceService, harness.Instances, null!, store, harness.FileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new VslAdoptionService(instanceService, harness.Instances, harness.GameVersions, null!, harness.FileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new VslAdoptionService(instanceService, harness.Instances, harness.GameVersions, store, null!, clock));
        Should.Throw<ArgumentNullException>(() => new VslAdoptionService(instanceService, harness.Instances, harness.GameVersions, store, harness.FileSystem, null!));
    }

    // ── Adoption d'installations : succès ───────────────────────────────────────────────

    [Fact]
    public async Task AdoptAsync_SingleInstallation_CreatesInstanceWithOriginalNameAndPinnedVersion()
    {
        var harness = Create();
        var installation = Installation();
        SeedInstallationFolder(harness.FileSystem, installation.Path);

        var outcome = await harness.Service.AdoptAsync([installation], [], progress: null, CancellationToken.None);

        var report = outcome.Installations.ShouldHaveSingleItem();
        report.Status.ShouldBe(VslInstallationAdoptionStatus.Adopted);
        report.Instance.ShouldNotBeNull();
        report.Instance!.Metadata.Name.ShouldBe("Survie médiévale");
        report.Instance.Metadata.GameVersion.ShouldBe(GameVersion.Parse("1.20.4"));
        report.Instance.Slug.ShouldBe("survie-medievale");
    }

    [Fact]
    public async Task AdoptAsync_SingleInstallation_CopiesDataFolderContentIntoInstanceData()
    {
        var harness = Create();
        var installation = Installation();
        SeedInstallationFolder(harness.FileSystem, installation.Path);

        var outcome = await harness.Service.AdoptAsync([installation], [], progress: null, CancellationToken.None);

        var slug = outcome.Installations.ShouldHaveSingleItem().Instance!.Slug;
        var dataDirectory = harness.Instances.GetDataDirectory(slug);
        harness.FileSystem.File.ReadAllText(harness.FileSystem.Path.Combine(dataDirectory, "Mods", "carrycapacity.zip")).ShouldBe("mod-content");
        harness.FileSystem.File.ReadAllText(harness.FileSystem.Path.Combine(dataDirectory, "Saves", "world.vcdbs")).ShouldBe("save-content");
        harness.FileSystem.File.ReadAllText(harness.FileSystem.Path.Combine(dataDirectory, "clientsettings.json")).ShouldBe("{}");
    }

    [Fact]
    public async Task AdoptAsync_SingleInstallation_DoesNotDeleteTheOriginalVslData()
    {
        // Adoption NON destructive : le dossier source doit rester intact après l'adoption.
        var harness = Create();
        var installation = Installation();
        SeedInstallationFolder(harness.FileSystem, installation.Path);

        await harness.Service.AdoptAsync([installation], [], progress: null, CancellationToken.None);

        harness.FileSystem.File.Exists(harness.FileSystem.Path.Combine(installation.Path, "Mods", "carrycapacity.zip")).ShouldBeTrue();
    }

    [Fact]
    public async Task AdoptAsync_SingleInstallation_ConvertsLaunchSettingsFromVslFields()
    {
        var harness = Create();
        var installation = Installation(startParams: "-logexcept -tracelog", envVars: "DXVK_HUD=fps", mesaGlThread: true);
        SeedInstallationFolder(harness.FileSystem, installation.Path);

        var outcome = await harness.Service.AdoptAsync([installation], [], progress: null, CancellationToken.None);

        var launch = outcome.Installations.ShouldHaveSingleItem().Instance!.Metadata.Launch;
        launch.ExtraArgs.ShouldBe(["-logexcept", "-tracelog"]);
        launch.Env.ShouldContainKeyAndValue("DXVK_HUD", "fps");
        launch.MesaGlThread.ShouldBeTrue();
    }

    [Fact]
    public async Task AdoptAsync_SingleInstallation_ConvertsPlaytimeMetadata()
    {
        var harness = Create();
        var installation = Installation(lastTimePlayedMs: 1770000000000L, totalTimePlayedMs: 3600000L);
        SeedInstallationFolder(harness.FileSystem, installation.Path);

        var outcome = await harness.Service.AdoptAsync([installation], [], progress: null, CancellationToken.None);

        var metadata = outcome.Installations.ShouldHaveSingleItem().Instance!.Metadata;
        metadata.LastLaunchedUtc.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1770000000000L));
        metadata.TotalPlaytimeSeconds.ShouldBe(3600L);
    }

    [Fact]
    public async Task AdoptAsync_InstallationNeverPlayed_LastLaunchedUtcStaysNull()
    {
        var harness = Create();
        var installation = Installation(lastTimePlayedMs: -1, totalTimePlayedMs: 0);
        SeedInstallationFolder(harness.FileSystem, installation.Path);

        var outcome = await harness.Service.AdoptAsync([installation], [], progress: null, CancellationToken.None);

        outcome.Installations.ShouldHaveSingleItem().Instance!.Metadata.LastLaunchedUtc.ShouldBeNull();
    }

    [Fact]
    public async Task AdoptAsync_AdoptedInstance_IsLoadableFromRepositoryAfterwards()
    {
        var harness = Create();
        var installation = Installation();
        SeedInstallationFolder(harness.FileSystem, installation.Path);

        var outcome = await harness.Service.AdoptAsync([installation], [], progress: null, CancellationToken.None);

        var slug = outcome.Installations.ShouldHaveSingleItem().Instance!.Slug;
        var reloaded = await harness.Instances.LoadAsync(slug, CancellationToken.None);
        reloaded.Metadata.Name.ShouldBe("Survie médiévale");
    }

    [Fact]
    public async Task AdoptAsync_TwoInstallationsWithTheSameName_AppendsNumericSuffixToTheSecondSlug()
    {
        var harness = Create();
        var first = Installation(name: "Survie", path: "/vsl/installations/a");
        var second = Installation(name: "Survie", path: "/vsl/installations/b");
        SeedInstallationFolder(harness.FileSystem, first.Path);
        SeedInstallationFolder(harness.FileSystem, second.Path);

        var outcome = await harness.Service.AdoptAsync([first, second], [], progress: null, CancellationToken.None);

        outcome.Installations[0].Instance!.Slug.ShouldBe("survie");
        outcome.Installations[1].Instance!.Slug.ShouldBe("survie-2");
    }

    [Fact]
    public async Task AdoptAsync_InstallationNameBlank_FallsBackToIdAsDisplayName()
    {
        var harness = Create();
        var installation = Installation(name: "") with { Id = "a1b2c3" };
        SeedInstallationFolder(harness.FileSystem, installation.Path);

        var outcome = await harness.Service.AdoptAsync([installation], [], progress: null, CancellationToken.None);

        var report = outcome.Installations.ShouldHaveSingleItem();
        report.SourceName.ShouldBe("a1b2c3");
        report.Instance!.Metadata.Name.ShouldBe("a1b2c3");
    }

    // ── Adoption d'installations : rapport par élément ──────────────────────────────────

    [Fact]
    public async Task AdoptAsync_UnparseableVersion_IsSkippedWithReason()
    {
        var harness = Create();
        var installation = Installation(version: "not-a-version");
        SeedInstallationFolder(harness.FileSystem, installation.Path);

        var outcome = await harness.Service.AdoptAsync([installation], [], progress: null, CancellationToken.None);

        var report = outcome.Installations.ShouldHaveSingleItem();
        report.Status.ShouldBe(VslInstallationAdoptionStatus.Skipped);
        report.Detail.ShouldNotBeNull();
        report.Instance.ShouldBeNull();
    }

    [Fact]
    public async Task AdoptAsync_UnparseableVersion_CreatesNoInstance()
    {
        var harness = Create();
        var installation = Installation(version: "not-a-version");
        SeedInstallationFolder(harness.FileSystem, installation.Path);

        await harness.Service.AdoptAsync([installation], [], progress: null, CancellationToken.None);

        (await harness.Instances.ScanAsync(CancellationToken.None)).Instances.ShouldBeEmpty();
    }

    [Fact]
    public async Task AdoptAsync_SourceFolderMissing_IsSkippedWithReason()
    {
        var harness = Create();
        var installation = Installation(path: "/vsl/installations/ghost");
        // Ne pose PAS le dossier source.

        var outcome = await harness.Service.AdoptAsync([installation], [], progress: null, CancellationToken.None);

        var report = outcome.Installations.ShouldHaveSingleItem();
        report.Status.ShouldBe(VslInstallationAdoptionStatus.Skipped);
    }

    [Fact]
    public async Task AdoptAsync_OneBadInstallationAmongGoodOnes_DoesNotBlockTheOthers()
    {
        var harness = Create();
        var good1 = Installation(name: "Bonne 1", path: "/vsl/installations/a");
        var bad = Installation(name: "Corrompue", path: "/vsl/installations/b", version: "n/a");
        var good2 = Installation(name: "Bonne 2", path: "/vsl/installations/c");
        SeedInstallationFolder(harness.FileSystem, good1.Path);
        SeedInstallationFolder(harness.FileSystem, good2.Path);
        // "bad" n'a pas de version lisible : peu importe qu'un dossier source existe.
        SeedInstallationFolder(harness.FileSystem, bad.Path);

        var outcome = await harness.Service.AdoptAsync([good1, bad, good2], [], progress: null, CancellationToken.None);

        outcome.AdoptedInstallationCount.ShouldBe(2);
        outcome.Installations[1].Status.ShouldBe(VslInstallationAdoptionStatus.Skipped);
        outcome.HasIssues.ShouldBeTrue();
    }

    [Fact]
    public async Task AdoptAsync_EmptySelection_ReturnsEmptyOutcomeAndCreatesNothing()
    {
        var harness = Create();

        var outcome = await harness.Service.AdoptAsync([], [], progress: null, CancellationToken.None);

        outcome.Installations.ShouldBeEmpty();
        outcome.Engines.ShouldBeEmpty();
        (await harness.Instances.ScanAsync(CancellationToken.None)).Instances.ShouldBeEmpty();
    }

    // ── Annulation ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdoptAsync_CancelledDuringInstallationCopy_RemovesThePartiallyCreatedInstance()
    {
        var harness = Create();
        var installation = Installation();
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(installation.Path, "a.txt"), new MockFileData("a"));
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(installation.Path, "b.txt"), new MockFileData("b"));
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<VslAdoptionProgress>(p =>
        {
            if (p.FileProgress?.FilesCopied == 1)
            {
                cts.Cancel();
            }
        });

        await Should.ThrowAsync<OperationCanceledException>(() => harness.Service.AdoptAsync([installation], [], progress, cts.Token));

        harness.Instances.Exists("survie-medievale").ShouldBeFalse();
    }

    [Fact]
    public async Task AdoptAsync_CancelledBetweenTwoInstallations_KeepsTheAlreadyAdoptedOne()
    {
        var harness = Create();
        var first = Installation(name: "Première", path: "/vsl/installations/a");
        var second = Installation(name: "Seconde", path: "/vsl/installations/b");
        SeedInstallationFolder(harness.FileSystem, first.Path);
        SeedInstallationFolder(harness.FileSystem, second.Path);
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<VslAdoptionProgress>(p =>
        {
            if (p.Phase == VslAdoptionPhase.AdoptingInstallations && p.CompletedItems == 1 && p.CurrentItemLabel == "Seconde")
            {
                cts.Cancel();
            }
        });

        await Should.ThrowAsync<OperationCanceledException>(() => harness.Service.AdoptAsync([first, second], [], progress, cts.Token));

        harness.Instances.Exists("premiere").ShouldBeTrue();
        harness.Instances.Exists("seconde").ShouldBeFalse();
    }

    // ── Progression ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdoptAsync_ReportsProgressWithLabelAndTotals()
    {
        var harness = Create();
        var installation = Installation();
        SeedInstallationFolder(harness.FileSystem, installation.Path);
        var reports = new List<VslAdoptionProgress>();

        await harness.Service.AdoptAsync([installation], [], new SynchronousProgress<VslAdoptionProgress>(reports.Add), CancellationToken.None);

        reports.ShouldContain(r => r.Phase == VslAdoptionPhase.AdoptingInstallations && r.CurrentItemLabel == "Survie médiévale");
        reports.ShouldContain(r => r.Phase == VslAdoptionPhase.AdoptingInstallations && r.CompletedItems == 1 && r.TotalItems == 1);
    }

    // ── Adoption de moteurs : succès ───────────────────────────────────────────────────

    [Fact]
    public async Task AdoptAsync_Engine_CopiesFilesAndWritesCompletionMarker()
    {
        var harness = Create();
        var engine = new VslGameVersionEntry { Version = "1.20.4", Path = "/vsl/gameversions/1.20.4" };
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(engine.Path, "Vintagestory"), new MockFileData("binaire"));

        var outcome = await harness.Service.AdoptAsync([], [engine], progress: null, CancellationToken.None);

        var report = outcome.Engines.ShouldHaveSingleItem();
        report.Status.ShouldBe(VslEngineAdoptionStatus.Adopted);
        harness.GameVersions.IsInstalled(GameVersion.Parse("1.20.4")).ShouldBeTrue();
        var installedFile = harness.FileSystem.Path.Combine(harness.GameVersions.GetVersionDirectory(GameVersion.Parse("1.20.4")), "Vintagestory");
        harness.FileSystem.File.ReadAllText(installedFile).ShouldBe("binaire");
    }

    [Fact]
    public async Task AdoptAsync_Engine_WritesProvenanceMarkerNamingTheSourcePath()
    {
        var harness = Create();
        var engine = new VslGameVersionEntry { Version = "1.20.4", Path = "/vsl/gameversions/1.20.4" };
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(engine.Path, "Vintagestory"), new MockFileData("binaire"));

        await harness.Service.AdoptAsync([], [engine], progress: null, CancellationToken.None);

        var provenancePath = harness.FileSystem.Path.Combine(
            harness.GameVersions.GetVersionDirectory(GameVersion.Parse("1.20.4")),
            VslEngineProvenance.FileName);
        harness.FileSystem.File.Exists(provenancePath).ShouldBeTrue();
        harness.FileSystem.File.ReadAllText(provenancePath).ShouldContain("/vsl/gameversions/1.20.4");
    }

    [Fact]
    public async Task AdoptAsync_Engine_DoesNotDeleteTheOriginalVslFiles()
    {
        var harness = Create();
        var engine = new VslGameVersionEntry { Version = "1.20.4", Path = "/vsl/gameversions/1.20.4" };
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(engine.Path, "Vintagestory"), new MockFileData("binaire"));

        await harness.Service.AdoptAsync([], [engine], progress: null, CancellationToken.None);

        harness.FileSystem.File.Exists(harness.FileSystem.Path.Combine(engine.Path, "Vintagestory")).ShouldBeTrue();
    }

    // ── Adoption de moteurs : rapport par élément ───────────────────────────────────────

    [Fact]
    public async Task AdoptAsync_EngineAlreadyInstalled_IsSkipped()
    {
        var harness = Create();
        await harness.GameVersions.MarkCompleteAsync(GameVersion.Parse("1.20.4"), CancellationToken.None);
        var engine = new VslGameVersionEntry { Version = "1.20.4", Path = "/vsl/gameversions/1.20.4" };
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(engine.Path, "Vintagestory"), new MockFileData("binaire"));

        var outcome = await harness.Service.AdoptAsync([], [engine], progress: null, CancellationToken.None);

        outcome.Engines.ShouldHaveSingleItem().Status.ShouldBe(VslEngineAdoptionStatus.Skipped);
    }

    [Fact]
    public async Task AdoptAsync_EngineUnparseableVersion_IsSkipped()
    {
        var harness = Create();
        var engine = new VslGameVersionEntry { Version = "not-a-version", Path = "/vsl/gameversions/x" };
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(engine.Path, "Vintagestory"), new MockFileData("binaire"));

        var outcome = await harness.Service.AdoptAsync([], [engine], progress: null, CancellationToken.None);

        outcome.Engines.ShouldHaveSingleItem().Status.ShouldBe(VslEngineAdoptionStatus.Skipped);
    }

    [Fact]
    public async Task AdoptAsync_EngineSourceFolderMissing_IsSkipped()
    {
        var harness = Create();
        var engine = new VslGameVersionEntry { Version = "1.20.4", Path = "/vsl/gameversions/ghost" };

        var outcome = await harness.Service.AdoptAsync([], [engine], progress: null, CancellationToken.None);

        outcome.Engines.ShouldHaveSingleItem().Status.ShouldBe(VslEngineAdoptionStatus.Skipped);
    }

    [Fact]
    public async Task AdoptAsync_CancelledDuringEngineCopy_RemovesThePartialVersionDirectoryEntirely()
    {
        var harness = Create();
        var engine = new VslGameVersionEntry { Version = "1.20.4", Path = "/vsl/gameversions/1.20.4" };
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(engine.Path, "a.dll"), new MockFileData("a"));
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(engine.Path, "b.dll"), new MockFileData("b"));
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<VslAdoptionProgress>(p =>
        {
            if (p.Phase == VslAdoptionPhase.AdoptingEngines && p.FileProgress?.FilesCopied == 1)
            {
                cts.Cancel();
            }
        });

        // Note : la copie d'un moteur ne relaie pas de progression fichier par fichier (voir
        // AdoptEngineAsync, progress: null passé à DirectoryCopier), donc l'annulation ici se fait
        // via le jeton directement plutôt que via un callback de progression.
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => harness.Service.AdoptAsync([], [engine], progress, cts.Token));

        harness.GameVersions.IsInstalled(GameVersion.Parse("1.20.4")).ShouldBeFalse();
        harness.FileSystem.Directory.Exists(harness.GameVersions.GetVersionDirectory(GameVersion.Parse("1.20.4"))).ShouldBeFalse();
    }

    // ── Installations et moteurs ensemble ───────────────────────────────────────────────

    [Fact]
    public async Task AdoptAsync_InstallationsAndEngines_ProcessesInstallationsBeforeEngines()
    {
        var harness = Create();
        var installation = Installation();
        SeedInstallationFolder(harness.FileSystem, installation.Path);
        var engine = new VslGameVersionEntry { Version = "1.20.4", Path = "/vsl/gameversions/1.20.4" };
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(engine.Path, "Vintagestory"), new MockFileData("binaire"));
        var phases = new List<VslAdoptionPhase>();

        await harness.Service.AdoptAsync(
            [installation],
            [engine],
            new SynchronousProgress<VslAdoptionProgress>(p => phases.Add(p.Phase)),
            CancellationToken.None);

        phases.ShouldContain(VslAdoptionPhase.AdoptingInstallations);
        phases.ShouldContain(VslAdoptionPhase.AdoptingEngines);
        phases.IndexOf(VslAdoptionPhase.AdoptingInstallations).ShouldBeLessThan(phases.LastIndexOf(VslAdoptionPhase.AdoptingEngines));
    }
}