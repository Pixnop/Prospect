using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;

using Prospect.Core.Backups;
using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Instances;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Backups;

/// <summary>
/// <see cref="InstanceBackupService"/> : création (zip réel de <c>data/</c>, horodatage IClock,
/// progression, annulation nettoyante), liste, suppression, rétention, restauration (échange sûr),
/// et la garantie de placement qui fait que la duplication d'instance ne copie jamais
/// <c>backups/</c> (voir la docstring de la classe testée).
/// </summary>
public sealed class InstanceBackupServiceTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly GameVersion SampleVersion = GameVersion.Parse("1.21.3");

    private sealed record Fixture(
        InstanceBackupService Backups,
        InstanceService Instances,
        IInstanceRepository Repository,
        MockFileSystem FileSystem,
        FakeClock Clock,
        string Slug);

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var instanceService = new InstanceService(repository, fileSystem, clock);
        var backupService = new InstanceBackupService(repository, fileSystem, clock);
        var record = await instanceService.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        return new Fixture(backupService, instanceService, repository, fileSystem, clock, record.Slug);
    }

    private static void SeedDataFile(Fixture fixture, string relativePath, string content)
    {
        var fullPath = fixture.FileSystem.Path.Combine(fixture.Repository.GetDataDirectory(fixture.Slug), relativePath);
        fixture.FileSystem.AddFile(fullPath, new MockFileData(content));
    }

    private static Dictionary<string, string> ReadZipContents(MockFileSystem fileSystem, string zipPath)
    {
        var stream = fileSystem.File.OpenRead(zipPath);
        using (stream)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var contents = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                contents[entry.FullName] = reader.ReadToEnd();
            }

            return contents;
        }
    }

    private static Dictionary<string, string> ReadDirectoryContents(MockFileSystem fileSystem, string directory)
    {
        if (!fileSystem.Directory.Exists(directory))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var files = fileSystem.Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var relative = fileSystem.Path.GetRelativePath(directory, file).Replace(fileSystem.Path.DirectorySeparatorChar, '/');
            contents[relative] = fileSystem.File.ReadAllText(file);
        }

        return contents;
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var clock = new FakeClock(Now);

        Should.Throw<ArgumentNullException>(() => new InstanceBackupService(null!, fileSystem, clock));
        Should.Throw<ArgumentNullException>(() => new InstanceBackupService(repository, null!, clock));
        Should.Throw<ArgumentNullException>(() => new InstanceBackupService(repository, fileSystem, null!));
    }

    // ── Emplacement (HORS data/, jamais copié par une duplication) ─────────────────────────────

    [Fact]
    public async Task GetBackupsDirectory_IsASiblingOfDataDirectory_NeverNestedInsideIt()
    {
        var fixture = await CreateFixtureAsync();

        var backupsDir = fixture.Backups.GetBackupsDirectory(fixture.Slug);
        var dataDir = fixture.Repository.GetDataDirectory(fixture.Slug);
        var instanceDir = fixture.Repository.GetInstanceDirectory(fixture.Slug);

        backupsDir.ShouldNotBe(dataDir);
        backupsDir.StartsWith(dataDir, StringComparison.Ordinal).ShouldBeFalse();
        // Comparé à une valeur reconstruite par le même Path.Combine plutôt qu'un aller-retour par
        // GetDirectoryName : AppPaths calcule ses chemins avec le System.IO.Path réel (voir sa
        // docstring) tandis que ce service passe par _fileSystem.Path, deux normalisations qui ne
        // s'accordent pas forcément caractère pour caractère sur un racine POSIX de test (« /... »)
        // exécuté sous Windows (le séparateur de tête ne survit pas à GetDirectoryName de la même
        // façon des deux côtés), sans que ça ne dise rien du vrai bug qu'on veut détecter ici.
        backupsDir.ShouldBe(fixture.FileSystem.Path.Combine(instanceDir, "backups"));
    }

    // ── Création ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_UnknownInstance_ThrowsInstanceNotFoundException()
    {
        var fixture = await CreateFixtureAsync();

        await Should.ThrowAsync<InstanceNotFoundException>(() => fixture.Backups.CreateAsync("ghost", progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ZipsDataDirectory_ProducesAReadableZipWithExactDataContents()
    {
        var fixture = await CreateFixtureAsync();
        SeedDataFile(fixture, "clientsettings.json", "{}");
        SeedDataFile(fixture, fixture.FileSystem.Path.Combine("Mods", "carrycapacity.zip"), "mod-bytes");
        SeedDataFile(fixture, fixture.FileSystem.Path.Combine("Saves", "world1.vcdbs"), "world-bytes");

        var info = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        var zipPath = fixture.FileSystem.Path.Combine(fixture.Backups.GetBackupsDirectory(fixture.Slug), info.FileName);
        fixture.FileSystem.File.Exists(zipPath).ShouldBeTrue();

        var zipContents = ReadZipContents(fixture.FileSystem, zipPath);
        var dataContents = ReadDirectoryContents(fixture.FileSystem, fixture.Repository.GetDataDirectory(fixture.Slug));
        zipContents.ShouldBe(dataContents);
    }

    [Fact]
    public async Task CreateAsync_EmptyDataDirectory_StillProducesAReadableEmptyZip()
    {
        var fixture = await CreateFixtureAsync();

        var info = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        var zipPath = fixture.FileSystem.Path.Combine(fixture.Backups.GetBackupsDirectory(fixture.Slug), info.FileName);
        ReadZipContents(fixture.FileSystem, zipPath).ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAsync_UsesIClockForTheFileNameTimestamp()
    {
        var fixture = await CreateFixtureAsync();

        var info = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        info.FileName.ShouldBe("20260810-140000.zip");
        info.CreatedUtc.ShouldBe(Now);
    }

    [Fact]
    public async Task CreateAsync_TwoBackupsAtTheSameClockInstant_GetDistinctFileNames()
    {
        var fixture = await CreateFixtureAsync();

        var first = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        var second = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        first.FileName.ShouldNotBe(second.FileName);
        (await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task CreateAsync_ReportsFilesProcessedAndTotal()
    {
        var fixture = await CreateFixtureAsync();
        SeedDataFile(fixture, "a.txt", "a");
        SeedDataFile(fixture, "b.txt", "b");
        var reports = new List<InstanceBackupProgress>();
        var progress = new SynchronousProgress<InstanceBackupProgress>(reports.Add);

        await fixture.Backups.CreateAsync(fixture.Slug, progress, CancellationToken.None);

        reports.Count.ShouldBe(2);
        reports.ShouldAllBe(r => r.TotalFiles == 2);
        reports.Select(r => r.FilesProcessed).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task CreateAsync_CancelledPartway_RemovesThePartialZipEntirelyAndThrows()
    {
        var fixture = await CreateFixtureAsync();
        SeedDataFile(fixture, "a.txt", "a");
        SeedDataFile(fixture, "b.txt", "b");
        SeedDataFile(fixture, "c.txt", "c");
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<InstanceBackupProgress>(report =>
        {
            if (report.FilesProcessed == 1)
            {
                cts.Cancel();
            }
        });

        await Should.ThrowAsync<OperationCanceledException>(() => fixture.Backups.CreateAsync(fixture.Slug, progress, cts.Token));

        var backupsDir = fixture.Backups.GetBackupsDirectory(fixture.Slug);
        fixture.FileSystem.Directory.Exists(backupsDir).ShouldBeTrue();
        fixture.FileSystem.Directory.GetFiles(backupsDir).ShouldBeEmpty();
        (await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    // ── Liste ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_NoBackupsYet_ReturnsEmpty()
    {
        var fixture = await CreateFixtureAsync();

        (await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAsync_MultipleBackups_OrdersNewestFirst()
    {
        var fixture = await CreateFixtureAsync();
        var first = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        fixture.Clock.UtcNow = Now.AddMinutes(5);
        var second = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        var listed = await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None);

        listed.Select(b => b.FileName).ShouldBe([second.FileName, first.FileName]);
    }

    [Fact]
    public async Task ListAsync_ReportsSizeMatchingTheZipFile()
    {
        var fixture = await CreateFixtureAsync();
        SeedDataFile(fixture, "clientsettings.json", "{ \"language\": \"fr\" }");

        var info = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        var listed = (await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None)).ShouldHaveSingleItem();

        var zipPath = fixture.FileSystem.Path.Combine(fixture.Backups.GetBackupsDirectory(fixture.Slug), info.FileName);
        listed.SizeInBytes.ShouldBe(fixture.FileSystem.FileInfo.New(zipPath).Length);
        listed.SizeInBytes.ShouldBeGreaterThan(0);
    }

    // ── Suppression ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingBackup_RemovesTheFile()
    {
        var fixture = await CreateFixtureAsync();
        var info = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        await fixture.Backups.DeleteAsync(fixture.Slug, info.FileName, CancellationToken.None);

        (await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_UnknownBackup_ThrowsInstanceBackupNotFoundException()
    {
        var fixture = await CreateFixtureAsync();

        var exception = await Should.ThrowAsync<InstanceBackupNotFoundException>(
            () => fixture.Backups.DeleteAsync(fixture.Slug, "20200101-000000.zip", CancellationToken.None));

        exception.Slug.ShouldBe(fixture.Slug);
        exception.FileName.ShouldBe("20200101-000000.zip");
    }

    // ── Rétention ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ExceedsKeepCount_PrunesOldestFirstAndNeverTheFreshOne()
    {
        var fixture = await CreateFixtureAsync();
        await fixture.Instances.UpdateBackupSettingsAsync(fixture.Slug, new InstanceBackupSettings { KeepCount = 2 }, CancellationToken.None);

        var first = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        var second = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        fixture.Clock.UtcNow = Now.AddMinutes(2);
        var third = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        var remaining = await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None);

        remaining.Select(b => b.FileName).ShouldBe([third.FileName, second.FileName], ignoreOrder: true);
        remaining.ShouldNotContain(b => b.FileName == first.FileName);
        remaining.ShouldContain(b => b.FileName == third.FileName); // la fraîche n'est jamais élaguée.
    }

    [Fact]
    public async Task CreateAsync_KeepCountNotExceeded_PrunesNothing()
    {
        var fixture = await CreateFixtureAsync();
        await fixture.Instances.UpdateBackupSettingsAsync(fixture.Slug, new InstanceBackupSettings { KeepCount = 5 }, CancellationToken.None);

        await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        fixture.Clock.UtcNow = Now.AddMinutes(1);
        await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        (await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None)).Count.ShouldBe(2);
    }

    // ── Duplication d'instance : jamais backups/ ────────────────────────────────────────────────

    [Fact]
    public async Task DuplicateAsync_SourceHasBackups_TargetHasNone()
    {
        var fixture = await CreateFixtureAsync();
        SeedDataFile(fixture, "clientsettings.json", "{}");
        await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        var duplicated = await fixture.Instances.DuplicateAsync(fixture.Slug, "Copie", progress: null, CancellationToken.None);

        (await fixture.Backups.ListAsync(duplicated.Slug, CancellationToken.None)).ShouldBeEmpty();
        fixture.FileSystem.Directory.Exists(fixture.Backups.GetBackupsDirectory(duplicated.Slug)).ShouldBeFalse();
        // La source, elle, garde sa sauvegarde : la duplication n'y touche pas non plus.
        (await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None)).ShouldHaveSingleItem();
    }

    // ── Restauration : échange sûr ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RestoreAsync_UnknownBackup_ThrowsInstanceBackupNotFoundException()
    {
        var fixture = await CreateFixtureAsync();

        await Should.ThrowAsync<InstanceBackupNotFoundException>(
            () => fixture.Backups.RestoreAsync(fixture.Slug, "20200101-000000.zip", progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task RestoreAsync_Success_DataMatchesTheRestoredZipExactly()
    {
        var fixture = await CreateFixtureAsync();
        SeedDataFile(fixture, "clientsettings.json", "{ \"version\": 1 }");
        SeedDataFile(fixture, fixture.FileSystem.Path.Combine("Saves", "world1.vcdbs"), "monde-original");
        var backup = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        // L'instance évolue après la sauvegarde : nouveau fichier, fichier existant modifié.
        fixture.FileSystem.File.WriteAllText(
            fixture.FileSystem.Path.Combine(fixture.Repository.GetDataDirectory(fixture.Slug), "clientsettings.json"), "{ \"version\": 2 }");
        SeedDataFile(fixture, fixture.FileSystem.Path.Combine("Saves", "world2.vcdbs"), "monde-plus-recent");

        await fixture.Backups.RestoreAsync(fixture.Slug, backup.FileName, progress: null, CancellationToken.None);

        var dataContents = ReadDirectoryContents(fixture.FileSystem, fixture.Repository.GetDataDirectory(fixture.Slug));
        dataContents.ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["clientsettings.json"] = "{ \"version\": 1 }",
            ["Saves/world1.vcdbs"] = "monde-original",
        });
    }

    [Fact]
    public async Task RestoreAsync_TakesASafetyBackupOfTheCurrentStateBeforeOverwriting()
    {
        var fixture = await CreateFixtureAsync();
        SeedDataFile(fixture, "a.txt", "premiere-version");
        var firstBackup = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        fixture.Clock.UtcNow = Now.AddMinutes(10);
        fixture.FileSystem.File.WriteAllText(
            fixture.FileSystem.Path.Combine(fixture.Repository.GetDataDirectory(fixture.Slug), "a.txt"), "deuxieme-version");

        fixture.Clock.UtcNow = Now.AddMinutes(20);
        await fixture.Backups.RestoreAsync(fixture.Slug, firstBackup.FileName, progress: null, CancellationToken.None);

        // Trois sauvegardes existent maintenant : la première (restaurée depuis), et la sauvegarde
        // de sécurité prise automatiquement juste avant l'échange (contenant "deuxieme-version").
        var listed = await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None);
        listed.Count.ShouldBe(2);
        var safetyBackupFileName = listed.Select(b => b.FileName).Single(name => name != firstBackup.FileName);

        var safetyZipPath = fixture.FileSystem.Path.Combine(fixture.Backups.GetBackupsDirectory(fixture.Slug), safetyBackupFileName);
        ReadZipContents(fixture.FileSystem, safetyZipPath)["a.txt"].ShouldBe("deuxieme-version");

        // Et data/ reflète bien le contenu restauré (la première sauvegarde), pas l'état de sécurité.
        fixture.FileSystem.File.ReadAllText(
            fixture.FileSystem.Path.Combine(fixture.Repository.GetDataDirectory(fixture.Slug), "a.txt")).ShouldBe("premiere-version");
    }

    [Fact]
    public async Task RestoreAsync_FailureDuringExtraction_LeavesDataDirectoryCompletelyIntact()
    {
        var fixture = await CreateFixtureAsync();
        SeedDataFile(fixture, "a.txt", "contenu-a");
        SeedDataFile(fixture, "b.txt", "contenu-b");
        var backup = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        fixture.Clock.UtcNow = Now.AddMinutes(5);
        using var cts = new CancellationTokenSource();
        // Annule après le PREMIER fichier extrait (pas le dernier) : l'échec tombe pendant
        // l'extraction en staging, avant que data/ n'ait été touché du tout.
        var progress = new SynchronousProgress<InstanceBackupProgress>(report =>
        {
            if (report.FilesProcessed == 1)
            {
                cts.Cancel();
            }
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => fixture.Backups.RestoreAsync(fixture.Slug, backup.FileName, progress, cts.Token));

        var dataDir = fixture.Repository.GetDataDirectory(fixture.Slug);
        fixture.FileSystem.Directory.Exists(dataDir).ShouldBeTrue();
        ReadDirectoryContents(fixture.FileSystem, dataDir).ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.txt"] = "contenu-a",
            ["b.txt"] = "contenu-b",
        });
        // Aucun résidu de dossier de travail.
        fixture.FileSystem.Directory.GetDirectories(fixture.Repository.GetInstanceDirectory(fixture.Slug))
            .ShouldNotContain(d => d.Contains("restore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RestoreAsync_FailureDuringSwap_RecoversDataDirectoryFromTheSafetyBackup()
    {
        var fixture = await CreateFixtureAsync();
        SeedDataFile(fixture, "a.txt", "contenu-a");
        SeedDataFile(fixture, "b.txt", "contenu-b");
        var backup = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        fixture.Clock.UtcNow = Now.AddMinutes(5);
        fixture.FileSystem.File.WriteAllText(
            fixture.FileSystem.Path.Combine(fixture.Repository.GetDataDirectory(fixture.Slug), "a.txt"), "contenu-a-modifie");

        using var cts = new CancellationTokenSource();
        // Annule au DERNIER fichier extrait : l'extraction se termine intégralement (staging prêt),
        // puis data/ est renommé de côté, et c'est SEULEMENT APRÈS ce renommage que la vérification
        // suivante voit l'annulation — la fenêtre exacte où l'échange peut échouer en cours de
        // route (voir la docstring de RestoreAsync).
        var progress = new SynchronousProgress<InstanceBackupProgress>(report =>
        {
            if (report.FilesProcessed == report.TotalFiles)
            {
                cts.Cancel();
            }
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => fixture.Backups.RestoreAsync(fixture.Slug, backup.FileName, progress, cts.Token));

        // data/ existe et son contenu est celui d'AVANT la tentative de restauration (récupéré via
        // le renommage annulé, qui a le même effet que la sauvegarde de sécurité prise à l'étape 1).
        var dataDir = fixture.Repository.GetDataDirectory(fixture.Slug);
        fixture.FileSystem.Directory.Exists(dataDir).ShouldBeTrue();
        fixture.FileSystem.File.ReadAllText(fixture.FileSystem.Path.Combine(dataDir, "a.txt")).ShouldBe("contenu-a-modifie");
        fixture.FileSystem.File.ReadAllText(fixture.FileSystem.Path.Combine(dataDir, "b.txt")).ShouldBe("contenu-b");

        // Et la sauvegarde de sécurité de cet état existe bel et bien, deuxième filet en plus du
        // renommage annulé.
        var listed = await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None);
        listed.Count.ShouldBe(2);
    }
}