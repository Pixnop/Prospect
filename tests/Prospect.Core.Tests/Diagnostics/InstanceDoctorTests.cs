using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

using Prospect.Core.Common;
using Prospect.Core.Diagnostics;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Runtime;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.ModDb;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Diagnostics;

/// <summary>
/// <see cref="InstanceDoctor"/> : les cinq vérifications dans leurs états ok/avertissement/erreur,
/// l'agrégation (<see cref="InstanceDoctorReport.Findings"/>) et le tri par sévérité
/// (<see cref="InstanceDoctorReport.WorstSeverity"/>). Harnais entièrement sur
/// <see cref="MockFileSystem"/> et <see cref="FakeProcessRunner"/> : aucune des dépendances
/// construites ici ne connaît de client HTTP, ce qui rend le caractère hors ligne du docteur vrai
/// par construction plutôt que par convention (voir aussi le test d'intégration Desktop qui le
/// prouve dynamiquement avec un gestionnaire HTTP qui échoue si on l'appelle).
/// </summary>
public sealed class InstanceDoctorTests
{
    private const string Slug = "homestead-121";
    private const string GameVersionText = "1.22.1";

    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(InstanceDoctor Doctor, MockFileSystem FileSystem, IInstalledModRepository Mods, FakeProcessRunner Runner);

    private static Harness Create()
    {
        var fileSystem = new MockFileSystem();

        // Espace disque abondant par défaut : les tests qui ne portent pas sur la vérification 5 ne
        // doivent pas voir un avertissement de disque plein contaminer WorstSeverity/IsAllClear.
        SeedDrive(fileSystem, availableFreeSpaceBytes: 100L * 1024 * 1024 * 1024);

        var clock = new FakeClock(Noon);
        var store = new JsonFileStore(fileSystem);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, store, new InstanceMetadataMigrationPipeline([]));
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var runner = new FakeProcessRunner();
        var dotnetLocator = new DotnetLocator(runner, fileSystem, clock);
        var archiveReader = new ModArchiveReader(fileSystem);
        var mods = new FileSystemInstalledModRepository(fileSystem, instances, archiveReader, new DisabledSuffixModStateConvention(), store);

        SeedInstance(fileSystem);

        var doctor = new InstanceDoctor(instances, versions, dotnetLocator, mods, fileSystem, Paths);

        return new Harness(doctor, fileSystem, mods, runner);
    }

    // Le nom de lecteur enregistré doit correspondre exactement à ce que
    // MockDriveInfoFactory.New(path) recalcule (fileSystem.Path.GetPathRoot(path), voir sa source) :
    // "/" sous Linux/macOS, mais autre chose sous Windows, où GetPathRoot d'un chemin sans lettre de
    // lecteur comme Paths.RootDirectory ("/data/prospect", convention partagée par tout ce fichier
    // de tests) ne vaut pas "/". Coder "/" en dur passait inaperçu sous Linux/macOS et cassait sous
    // Windows (« Could not find file » à la lecture d'AvailableFreeSpace) : recalculer la clé de la
    // même façon que le fera l'appelant réel la rend correcte quel que soit l'OS d'exécution.
    private static void SeedDrive(MockFileSystem fileSystem, long availableFreeSpaceBytes, long totalSizeBytes = 500L * 1024 * 1024 * 1024)
        => fileSystem.AddDrive(
            fileSystem.Path.GetPathRoot(Paths.RootDirectory) ?? Paths.RootDirectory,
            new MockDriveData { AvailableFreeSpace = availableFreeSpaceBytes, TotalSize = totalSizeBytes });

    private static void SeedInstance(MockFileSystem fileSystem)
    {
        var metadata = new InstanceMetadata
        {
            SchemaVersion = InstanceMetadata.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            Name = "Homestead 1.21",
            GameVersion = GameVersion.Parse(GameVersionText),
            CreatedUtc = Noon,
        };

        fileSystem.AddFile(
            fileSystem.Path.Combine(Paths.InstancesDirectory, Slug, "instance.json"),
            new MockFileData(JsonSerializer.Serialize(metadata, InstanceJsonContext.Default.InstanceMetadata)));
        fileSystem.AddDirectory(fileSystem.Path.Combine(Paths.InstancesDirectory, Slug, "data", "Mods"));
    }

    // Chemin recombiné via l'IFileSystem abstrait plutôt qu'une concaténation littérale avec "/" :
    // Paths.VersionsDirectory est déjà séparé à la convention de l'OS courant (AppPaths), une
    // concaténation en dur cassait donc sous Windows (\ et / mélangés) tout en passant inaperçue
    // sous Linux/macOS, où "/" est déjà le séparateur natif.
    private static string VersionDirectory(Harness harness) => harness.FileSystem.Path.Combine(Paths.VersionsDirectory, GameVersionText);

    private static void SeedGameVersionInstalled(Harness harness)
    {
        var directory = VersionDirectory(harness);
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(directory, "Vintagestory"), new MockFileData("binaire"));
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(directory, FileSystemInstalledGameVersionRepository.CompletionMarkerFileName),
            new MockFileData(GameVersionText));
    }

    private static void SeedGameVersionIncomplete(Harness harness)
        => harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(VersionDirectory(harness), "Vintagestory"), new MockFileData("binaire"));

    private static void WriteRuntimeConfig(Harness harness, string frameworkName, string version)
    {
        var json = $$"""{ "runtimeOptions": { "framework": { "name": "{{frameworkName}}", "version": "{{version}}" } } }""";
        harness.FileSystem.AddFile(harness.FileSystem.Path.Combine(VersionDirectory(harness), "Vintagestory.runtimeconfig.json"), new MockFileData(json));
    }

    private static string ModInfoJson(string modId, string name, string version, params (string Id, string Requirement)[] dependencies)
    {
        var depsJson = string.Join(", ", dependencies.Select(dep => $$"""
        "{{dep.Id}}": "{{dep.Requirement}}"
        """));

        return $$"""
        { "type": "code", "modid": "{{modId}}", "name": "{{name}}", "version": "{{version}}",
          "authors": ["Quelqu'un"], "dependencies": { {{depsJson}} } }
        """;
    }

    private static async Task<InstalledMod> SeedModAsync(
        Harness harness,
        string modId,
        string version,
        bool enabled = true,
        (string Id, string Requirement)[]? dependencies = null,
        bool withProvenance = false,
        bool approximateMatch = false)
    {
        var modsDirectory = harness.Mods.GetModsDirectory(Slug);
        var stableName = $"{modId}-{version}.zip";
        var diskName = enabled ? stableName : stableName + ".disabled";
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(modsDirectory, diskName),
            new MockFileData(ModInfoSamples.BuildArchive(ModInfoJson(modId, modId, version, dependencies ?? []))));

        if (withProvenance)
        {
            await harness.Mods.SaveProvenanceAsync(
                Slug,
                new ModProvenance
                {
                    FileName = stableName,
                    ModId = 1,
                    ModIdString = modId,
                    ReleaseId = 1,
                    FileId = 1,
                    Version = ModVersion.Parse(version),
                    InstalledUtc = Noon,
                    ApproximateMatch = approximateMatch,
                },
                CancellationToken.None).ConfigureAwait(false);
        }

        var installed = await harness.Mods.ScanAsync(Slug, CancellationToken.None).ConfigureAwait(false);

        return installed.Single(mod => mod.FileName == stableName);
    }

    private static void SeedUnidentifiedMod(Harness harness, string fileName, bool enabled = true)
    {
        var modsDirectory = harness.Mods.GetModsDirectory(Slug);
        var diskName = enabled ? fileName : fileName + ".disabled";
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(modsDirectory, diskName),
            new MockFileData(ModInfoSamples.BuildArchive(null)));
    }

    private static ModUpdateResult UpdateResult(InstalledMod mod, ModUpdateStatus status) => new(mod, status);

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var harness = Create();
        var instances = new FileSystemInstanceRepository(harness.FileSystem, Paths, new JsonFileStore(harness.FileSystem), new InstanceMetadataMigrationPipeline([]));
        var versions = new FileSystemInstalledGameVersionRepository(harness.FileSystem, Paths);
        var dotnetLocator = new DotnetLocator(harness.Runner, harness.FileSystem, new FakeClock(Noon));

        Should.Throw<ArgumentNullException>(() => new InstanceDoctor(null!, versions, dotnetLocator, harness.Mods, harness.FileSystem, Paths));
        Should.Throw<ArgumentNullException>(() => new InstanceDoctor(instances, null!, dotnetLocator, harness.Mods, harness.FileSystem, Paths));
        Should.Throw<ArgumentNullException>(() => new InstanceDoctor(instances, versions, null!, harness.Mods, harness.FileSystem, Paths));
        Should.Throw<ArgumentNullException>(() => new InstanceDoctor(instances, versions, dotnetLocator, null!, harness.FileSystem, Paths));
        Should.Throw<ArgumentNullException>(() => new InstanceDoctor(instances, versions, dotnetLocator, harness.Mods, null!, Paths));
        Should.Throw<ArgumentNullException>(() => new InstanceDoctor(instances, versions, dotnetLocator, harness.Mods, harness.FileSystem, null!));
    }

    [Fact]
    public async Task DiagnoseAsync_UnknownSlug_ThrowsInstanceNotFoundException()
    {
        var harness = Create();

        await Should.ThrowAsync<InstanceNotFoundException>(() => harness.Doctor.DiagnoseAsync("ne-existe-pas", cancellationToken: CancellationToken.None));
    }

    // ── Vérification 1 : version du jeu ──────────────────────────────────────────────

    [Fact]
    public async Task GameVersion_InstalledAndComplete_IsOk()
    {
        var harness = Create();
        SeedGameVersionInstalled(harness);

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.GameVersion.Status.ShouldBe(GameVersionDoctorStatus.Installed);
        report.GameVersion.Severity.ShouldBe(InstanceDoctorSeverity.Ok);
    }

    [Fact]
    public async Task GameVersion_FolderPresentButNoCompletionMarker_IsIncompleteError()
    {
        var harness = Create();
        SeedGameVersionIncomplete(harness);

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.GameVersion.Status.ShouldBe(GameVersionDoctorStatus.Incomplete);
        report.GameVersion.Severity.ShouldBe(InstanceDoctorSeverity.Error);
    }

    [Fact]
    public async Task GameVersion_NoFolderAtAll_IsMissingError()
    {
        var harness = Create();

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.GameVersion.Status.ShouldBe(GameVersionDoctorStatus.Missing);
        report.GameVersion.Severity.ShouldBe(InstanceDoctorSeverity.Error);
    }

    // ── Vérification 2 : runtime .NET ────────────────────────────────────────────────

    [Fact]
    public async Task Runtime_RequiredVersionInstalled_IsPresent()
    {
        var harness = Create();
        SeedGameVersionInstalled(harness);
        WriteRuntimeConfig(harness, "Microsoft.NETCore.App", "8.0.10");
        harness.Runner.StandardOutput = "Microsoft.NETCore.App 8.0.10 [/usr/share/dotnet/shared/Microsoft.NETCore.App]";

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.Runtime.Availability.ShouldBe(RuntimeAvailability.Present);
    }

    [Fact]
    public async Task Runtime_RequiredVersionAbsent_IsMissingErrorWithExactVersion()
    {
        var harness = Create();
        SeedGameVersionInstalled(harness);
        WriteRuntimeConfig(harness, "Microsoft.NETCore.App", "10.0.0");
        harness.Runner.StandardOutput = "Microsoft.NETCore.App 8.0.10 [/path]";

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.Runtime.Availability.ShouldBe(RuntimeAvailability.Missing);
        report.Runtime.Requirement.FrameworkName.ShouldBe("Microsoft.NETCore.App");
        report.Runtime.Requirement.Version.ShouldBe(new Version(10, 0, 0));
        report.Findings.Single(finding => finding.Check == InstanceDoctorCheck.Runtime).Severity.ShouldBe(InstanceDoctorSeverity.Error);
    }

    [Fact]
    public async Task Runtime_NoRuntimeConfigToRead_IsIndeterminateWarning()
    {
        // Ni version installée, ni runtimeconfig.json : ReadRequirementAsync rend Unknown de
        // lui-même (même règle que GameLauncher), sans court-circuit explicite dans le docteur.
        var harness = Create();

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.Runtime.Availability.ShouldBe(RuntimeAvailability.Indeterminate);
        report.Findings.Single(finding => finding.Check == InstanceDoctorCheck.Runtime).Severity.ShouldBe(InstanceDoctorSeverity.Warning);
    }

    // ── Vérification 3 : dépendances de mods ─────────────────────────────────────────

    [Fact]
    public async Task ModDependencies_AllDeclaredDependenciesSatisfied_NoIssues()
    {
        var harness = Create();
        await SeedModAsync(harness, "vsimgui", "1.3.0");
        await SeedModAsync(harness, "configlib", "1.12.0", dependencies: [("vsimgui", "1.0.0")]);

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.ModIssues.ShouldBeEmpty();
    }

    [Fact]
    public async Task ModDependencies_DeclaredDependencyMissing_IsError()
    {
        var harness = Create();
        await SeedModAsync(harness, "configlib", "1.12.0", dependencies: [("vsimgui", "1.0.0")]);

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        var issue = report.ModIssues.ShouldHaveSingleItem();
        issue.Kind.ShouldBe(ModDoctorIssueKind.UnsatisfiedDependency);
        issue.ModDisplayName.ShouldBe("configlib");
        issue.Dependency.ShouldNotBeNull();
        issue.Dependency!.ModIdString.ShouldBe("vsimgui");
        issue.Dependency.Status.ShouldBe(ModDependencyStatus.Missing);
        issue.Severity.ShouldBe(InstanceDoctorSeverity.Error);
    }

    [Fact]
    public async Task ModDependencies_ProvidingModIsDisabled_DependentReportsAnError()
    {
        // Un mod désactivé « ne fournit rien » : le fournisseur existe sur le disque mais le jeu ne
        // le chargera pas, donc la dépendance du candidat actif reste non satisfaite.
        var harness = Create();
        await SeedModAsync(harness, "vsimgui", "1.3.0", enabled: false);
        await SeedModAsync(harness, "configlib", "1.12.0", dependencies: [("vsimgui", "1.0.0")]);

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        var issue = report.ModIssues.ShouldHaveSingleItem();
        issue.Dependency!.Status.ShouldBe(ModDependencyStatus.Disabled);
        issue.Severity.ShouldBe(InstanceDoctorSeverity.Error);
    }

    [Fact]
    public async Task ModDependencies_CandidateModItselfDisabled_ItsOwnMissingDependencyIsNotReported()
    {
        // Cohérence avec ModDependencyResolver.Evaluate (vérification inverse) : un mod désactivé ne
        // sera pas chargé, ses propres dépendances non satisfaites ne sont donc pas un problème tant
        // qu'il reste éteint.
        var harness = Create();
        await SeedModAsync(harness, "configlib", "1.12.0", enabled: false, dependencies: [("vsimgui", "1.0.0")]);

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.ModIssues.ShouldBeEmpty();
    }

    [Fact]
    public async Task ModDependencies_UnidentifiedArchive_IsWarningRegardlessOfEnabledState()
    {
        var harness = Create();
        SeedUnidentifiedMod(harness, "mystere.zip", enabled: false);

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        var issue = report.ModIssues.ShouldHaveSingleItem();
        issue.Kind.ShouldBe(ModDoctorIssueKind.Unidentified);
        issue.Severity.ShouldBe(InstanceDoctorSeverity.Warning);
    }

    [Fact]
    public async Task ModDependencies_TooOldInstalledVersion_IsError()
    {
        var harness = Create();
        await SeedModAsync(harness, "vsimgui", "1.0.0");
        await SeedModAsync(harness, "configlib", "1.12.0", dependencies: [("vsimgui", "1.5.0")]);

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        var issue = report.ModIssues.ShouldHaveSingleItem();
        issue.Dependency!.Status.ShouldBe(ModDependencyStatus.TooOld);
        issue.Severity.ShouldBe(InstanceDoctorSeverity.Error);
    }

    // ── Vérification 4 : compatibilité de version de jeu ─────────────────────────────

    [Fact]
    public async Task ModCompatibility_NoModsInstalled_IsOk()
    {
        var harness = Create();

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.ModCompatibility.TotalChecked.ShouldBe(0);
        report.ModCompatibility.Severity.ShouldBe(InstanceDoctorSeverity.Ok);
    }

    [Fact]
    public async Task ModCompatibility_ExactProvenanceMatch_IsConfirmedOk()
    {
        var harness = Create();
        await SeedModAsync(harness, "configlib", "1.12.0", withProvenance: true, approximateMatch: false);

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.ModCompatibility.ConfirmedCount.ShouldBe(1);
        report.ModCompatibility.TotalChecked.ShouldBe(1);
        report.ModCompatibility.Severity.ShouldBe(InstanceDoctorSeverity.Ok);
    }

    [Fact]
    public async Task ModCompatibility_ApproximateProvenanceMatch_IsWarning()
    {
        var harness = Create();
        await SeedModAsync(harness, "configlib", "1.12.0", withProvenance: true, approximateMatch: true);

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.ModCompatibility.ApproximateCount.ShouldBe(1);
        report.ModCompatibility.Severity.ShouldBe(InstanceDoctorSeverity.Warning);
        report.ModCompatibility.IsWhollyUnknown.ShouldBeFalse();
    }

    [Fact]
    public async Task ModCompatibility_NoProvenanceAndNoLastCheck_IsWhollyUnknownRatherThanInvented()
    {
        // Mod déposé à la main : aucune provenance ModDB, et aucune vérification de mises à jour
        // n'a eu lieu cette session. Rien de local ne permet de juger — le docteur ne doit rien
        // inventer.
        var harness = Create();
        await SeedModAsync(harness, "configlib", "1.12.0", withProvenance: false);

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.ModCompatibility.UnknownCount.ShouldBe(1);
        report.ModCompatibility.IsWhollyUnknown.ShouldBeTrue();
        report.ModCompatibility.Severity.ShouldBe(InstanceDoctorSeverity.Warning);
    }

    [Fact]
    public async Task ModCompatibility_DisabledOrUnidentifiedMods_AreExcludedFromTheCount()
    {
        var harness = Create();
        await SeedModAsync(harness, "configlib", "1.12.0", enabled: false, withProvenance: true, approximateMatch: true);
        SeedUnidentifiedMod(harness, "mystere.zip");

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.ModCompatibility.TotalChecked.ShouldBe(0);
        report.ModCompatibility.Severity.ShouldBe(InstanceDoctorSeverity.Ok);
    }

    [Fact]
    public async Task ModCompatibility_LastUpdateCheckKnowsTheMod_TakesPrecedenceOverProvenance()
    {
        var harness = Create();
        // Provenance approximative, mais la dernière vérification connaît ce fichier et le dit à
        // jour : le résultat le plus récent l'emporte.
        var mod = await SeedModAsync(harness, "configlib", "1.12.0", withProvenance: true, approximateMatch: true);
        var lastCheck = new InstanceUpdateReport([UpdateResult(mod, ModUpdateStatus.UpToDate)], Noon);

        var report = await harness.Doctor.DiagnoseAsync(Slug, lastCheck, CancellationToken.None);

        report.ModCompatibility.ConfirmedCount.ShouldBe(1);
        report.ModCompatibility.ApproximateCount.ShouldBe(0);
    }

    [Fact]
    public async Task ModCompatibility_LastUpdateCheckSaysUnknownToModDb_IsUnknown()
    {
        var harness = Create();
        var mod = await SeedModAsync(harness, "configlib", "1.12.0", withProvenance: true, approximateMatch: false);
        var lastCheck = new InstanceUpdateReport([UpdateResult(mod, ModUpdateStatus.UnknownToModDb)], Noon);

        var report = await harness.Doctor.DiagnoseAsync(Slug, lastCheck, CancellationToken.None);

        report.ModCompatibility.UnknownCount.ShouldBe(1);
        report.ModCompatibility.ConfirmedCount.ShouldBe(0);
    }

    [Fact]
    public async Task ModCompatibility_LastUpdateCheckDoesNotMentionThisFile_FallsBackToProvenance()
    {
        var harness = Create();
        var mod = await SeedModAsync(harness, "configlib", "1.12.0", withProvenance: true, approximateMatch: false);
        var otherMod = await SeedModAsync(harness, "vsimgui", "1.0.0", withProvenance: true, approximateMatch: false);
        var lastCheck = new InstanceUpdateReport([UpdateResult(otherMod, ModUpdateStatus.UpToDate)], Noon);

        var report = await harness.Doctor.DiagnoseAsync(Slug, lastCheck, CancellationToken.None);

        report.ModCompatibility.TotalChecked.ShouldBe(2);
        report.ModCompatibility.ConfirmedCount.ShouldBe(2);
        _ = mod;
    }

    // ── Vérification 5 : espace disque ───────────────────────────────────────────────

    [Fact]
    public async Task DiskSpace_PlentyOfFreeSpace_IsOk()
    {
        var harness = Create();

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.DiskSpace.IsLow.ShouldBeFalse();
        report.DiskSpace.Severity.ShouldBe(InstanceDoctorSeverity.Ok);
    }

    [Fact]
    public async Task DiskSpace_BelowThreshold_IsWarning()
    {
        var fileSystem = new MockFileSystem();
        SeedDrive(fileSystem, availableFreeSpaceBytes: 512L * 1024 * 1024);
        var clock = new FakeClock(Noon);
        var store = new JsonFileStore(fileSystem);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, store, new InstanceMetadataMigrationPipeline([]));
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var runner = new FakeProcessRunner();
        var dotnetLocator = new DotnetLocator(runner, fileSystem, clock);
        var archiveReader = new ModArchiveReader(fileSystem);
        var mods = new FileSystemInstalledModRepository(fileSystem, instances, archiveReader, new DisabledSuffixModStateConvention(), store);
        SeedInstance(fileSystem);
        var doctor = new InstanceDoctor(instances, versions, dotnetLocator, mods, fileSystem, Paths);

        var report = await doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.DiskSpace.AvailableBytes.ShouldBe(512L * 1024 * 1024);
        report.DiskSpace.IsLow.ShouldBeTrue();
        report.DiskSpace.Severity.ShouldBe(InstanceDoctorSeverity.Warning);
    }

    // ── Agrégation et tri par sévérité ────────────────────────────────────────────────

    [Fact]
    public async Task Findings_EverythingHealthy_IsAllClear()
    {
        var harness = Create();
        SeedGameVersionInstalled(harness);
        WriteRuntimeConfig(harness, "Microsoft.NETCore.App", "8.0.10");
        harness.Runner.StandardOutput = "Microsoft.NETCore.App 8.0.10 [/path]";
        await SeedModAsync(harness, "configlib", "1.12.0", withProvenance: true, approximateMatch: false);

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.IsAllClear.ShouldBeTrue();
        report.WorstSeverity.ShouldBe(InstanceDoctorSeverity.Ok);
        report.Findings.ShouldAllBe(finding => finding.Severity == InstanceDoctorSeverity.Ok);
    }

    [Fact]
    public async Task Findings_MixOfSeverities_WorstSeverityIsError()
    {
        var harness = Create();
        // Version absente (erreur) + mod non identifié (avertissement) : le pire l'emporte.
        SeedUnidentifiedMod(harness, "mystere.zip");

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        report.Findings.ShouldContain(finding => finding.Check == InstanceDoctorCheck.GameVersion && finding.Severity == InstanceDoctorSeverity.Error);
        report.Findings.ShouldContain(finding => finding.Check == InstanceDoctorCheck.ModDependencies && finding.Severity == InstanceDoctorSeverity.Warning);
        report.WorstSeverity.ShouldBe(InstanceDoctorSeverity.Error);
        report.IsAllClear.ShouldBeFalse();
    }

    [Fact]
    public async Task Findings_CountsOneEntryPerCheckPlusOnePerModIssue()
    {
        var harness = Create();
        await SeedModAsync(harness, "configlib", "1.12.0", dependencies: [("vsimgui", "1.0.0")]);
        SeedUnidentifiedMod(harness, "mystere.zip");

        var report = await harness.Doctor.DiagnoseAsync(Slug, cancellationToken: CancellationToken.None);

        // GameVersion + Runtime + ModCompatibility + DiskSpace (4, fixes) + 2 lignes de mods (une
        // dépendance manquante, une archive non identifiée).
        report.Findings.Count.ShouldBe(6);
        report.ModIssues.Count.ShouldBe(2);
    }
}