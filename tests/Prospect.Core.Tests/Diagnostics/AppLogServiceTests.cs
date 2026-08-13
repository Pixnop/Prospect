using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;

using Prospect.Core.Common;
using Prospect.Core.Diagnostics;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Diagnostics;

/// <summary>
/// La lecture et l'emport du dossier <c>logs/</c>, ce que la page Journaux montre et ce que le
/// bouton d'export met dans le zip joint à un rapport.
/// </summary>
public sealed class AppLogServiceTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 13, 12, 30, 15, TimeSpan.Zero);

    private static (AppLogService Service, MockFileSystem FileSystem) Create()
    {
        var fileSystem = new MockFileSystem();

        return (new AppLogService(fileSystem, Paths), fileSystem);
    }

    private static void Seed(MockFileSystem fileSystem, params string[] lines)
        => fileSystem.AddFile(
            fileSystem.Path.Combine(Paths.LogsDirectory, FileAppLog.FileName),
            new MockFileData(string.Join(Environment.NewLine, lines) + Environment.NewLine));

    [Fact]
    public void ReadTail_WithoutAnyLogYet_IsEmptyRatherThanAnError()
    {
        var (service, _) = Create();

        service.ReadTail().ShouldBeEmpty();
    }

    [Fact]
    public void ReadTail_ReadsTheLevelOfEachLine()
    {
        var (service, fileSystem) = Create();
        Seed(
            fileSystem,
            "2026-08-13T12:30:15Z [INFO] Version 1.22.6 installée.",
            "2026-08-13T12:30:16Z [WARN] Miroir cdn injoignable, bascule sur local.",
            "2026-08-13T12:30:17Z [ERROR] Version 1.22.6 : aucun exécutable attendu.");

        var entries = service.ReadTail();

        entries.Select(entry => entry.Level).ShouldBe([AppLogLevel.Info, AppLogLevel.Warning, AppLogLevel.Error]);
        entries[^1].Text.ShouldContain("aucun exécutable attendu");
    }

    /// <summary>
    /// Une ligne sans entête — la suite d'une trace d'exception, ou le début d'un fichier tronqué
    /// par le plafond de taille — s'affiche telle quelle, sans niveau inventé.
    /// </summary>
    [Fact]
    public void ReadTail_ALineWithoutAHeader_HasNoLevel()
    {
        var (service, fileSystem) = Create();
        Seed(fileSystem, "   at Prospect.Core.GameVersions.GameInstallService.InstallAsync()");

        service.ReadTail().ShouldHaveSingleItem().Level.ShouldBeNull();
    }

    /// <summary>
    /// Le niveau se lit dans l'ENTÊTE, pas n'importe où : un message qui cite une étiquette ne doit
    /// pas se teindre en rouge pour autant.
    /// </summary>
    [Fact]
    public void ReadTail_AMessageQuotingALabel_IsNotMistakenForThatLevel()
    {
        var (service, fileSystem) = Create();
        Seed(fileSystem, "2026-08-13T12:30:15Z [INFO] La ligne rapportée était « [ERROR] rien à signaler ».");

        service.ReadTail().ShouldHaveSingleItem().Level.ShouldBe(AppLogLevel.Info);
    }

    [Fact]
    public void ReadTail_KeepsTheLastLinesInReadingOrder()
    {
        var (service, fileSystem) = Create();
        Seed(fileSystem, [.. Enumerable.Range(1, 50).Select(index => $"2026-08-13T12:30:15Z [INFO] ligne {index}")]);

        var entries = service.ReadTail(maxLines: 3);

        entries.Select(entry => entry.Text).ShouldBe(
        [
            "2026-08-13T12:30:15Z [INFO] ligne 48",
            "2026-08-13T12:30:15Z [INFO] ligne 49",
            "2026-08-13T12:30:15Z [INFO] ligne 50",
        ]);
    }

    /// <summary>
    /// Le journal se termine par un saut de ligne : la dernière « ligne » du fichier est vide et ne
    /// doit pas manger une place dans la fin affichée.
    /// </summary>
    [Fact]
    public void ReadTail_TheTrailingNewline_DoesNotCountAsALine()
    {
        var (service, fileSystem) = Create();
        Seed(fileSystem, "2026-08-13T12:30:15Z [INFO] seule ligne");

        service.ReadTail().ShouldHaveSingleItem().Text.ShouldEndWith("seule ligne");
    }

    [Fact]
    public void ReadTail_ANonPositiveCount_IsRejected()
    {
        var (service, _) = Create();

        Should.Throw<ArgumentOutOfRangeException>(() => service.ReadTail(0));
    }

    [Fact]
    public void FindLogFiles_WithoutALogsFolder_IsEmpty()
        => Create().Service.FindLogFiles().ShouldBeEmpty();

    [Fact]
    public void FindLogFiles_TheAppLogFirstThenTheInstanceLogs()
    {
        var (service, fileSystem) = Create();
        Seed(fileSystem, "2026-08-13T12:30:15Z [INFO] démarrage");
        fileSystem.AddFile(fileSystem.Path.Combine(Paths.LogsDirectory, "instance-survie.log"), new MockFileData("jeu"));
        fileSystem.AddFile(fileSystem.Path.Combine(Paths.LogsDirectory, "instance-bac-a-sable.log"), new MockFileData("jeu"));

        // Un fichier qui n'est pas un journal reste dehors : l'export est une pièce jointe de
        // rapport, pas une copie du dossier.
        fileSystem.AddFile(fileSystem.Path.Combine(Paths.LogsDirectory, "notes.txt"), new MockFileData("perso"));

        service.FindLogFiles().Select(fileSystem.Path.GetFileName).ShouldBe(
            ["prospect.log", "instance-bac-a-sable.log", "instance-survie.log"]);
    }

    [Fact]
    public async Task ExportAsync_WritesAZipCarryingEveryLog()
    {
        var (service, fileSystem) = Create();
        Seed(fileSystem, "2026-08-13T12:30:15Z [INFO] démarrage");
        fileSystem.AddFile(fileSystem.Path.Combine(Paths.LogsDirectory, "instance-survie.log"), new MockFileData("sortie du jeu"));
        const string Destination = "/home/jean/prospect-journaux.zip";

        var written = await service.ExportAsync(Destination, CancellationToken.None);

        written.ShouldBe(2);
        using var archive = new ZipArchive(fileSystem.File.OpenRead(Destination), ZipArchiveMode.Read);
        archive.Entries.Select(entry => entry.FullName).ShouldBe(["prospect.log", "instance-survie.log"]);
        using var reader = new StreamReader(archive.GetEntry("instance-survie.log")!.Open());
        reader.ReadToEnd().ShouldBe("sortie du jeu");
    }

    /// <summary>
    /// Aucun journal : l'archive est quand même écrite, vide. L'utilisateur a choisi une
    /// destination, et un zip sans entrée se comprend mieux qu'un échec.
    /// </summary>
    [Fact]
    public async Task ExportAsync_WithoutAnyLog_WritesAnEmptyArchive()
    {
        var (service, fileSystem) = Create();
        const string Destination = "/home/jean/prospect-journaux.zip";

        (await service.ExportAsync(Destination, CancellationToken.None)).ShouldBe(0);

        using var archive = new ZipArchive(fileSystem.File.OpenRead(Destination), ZipArchiveMode.Read);
        archive.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExportAsync_CreatesTheDestinationFolderWhenItIsMissing()
    {
        var (service, fileSystem) = Create();
        Seed(fileSystem, "2026-08-13T12:30:15Z [INFO] démarrage");

        await service.ExportAsync("/home/jean/rapports/2026/journaux.zip", CancellationToken.None);

        fileSystem.File.Exists("/home/jean/rapports/2026/journaux.zip").ShouldBeTrue();
    }

    [Fact]
    public async Task ExportAsync_AnEmptyDestination_IsRejected()
    {
        var (service, _) = Create();

        await Should.ThrowAsync<ArgumentException>(() => service.ExportAsync(string.Empty, CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new AppLogService(null!, Paths));
        Should.Throw<ArgumentNullException>(() => new AppLogService(new MockFileSystem(), null!));
    }

    /// <summary>
    /// Le service lit ce que <see cref="FileAppLog"/> écrit : les deux moitiés du contrat sont
    /// vérifiées ensemble, pas chacune contre sa propre idée du format.
    /// </summary>
    [Fact]
    public void ReadTail_ReadsBackWhatFileAppLogWrote()
    {
        var fileSystem = new MockFileSystem();
        var log = new FileAppLog(fileSystem, Paths, new FakeClock(Noon));
        log.Write(AppLogLevel.Error, "Version 1.22.6 : aucun exécutable attendu.");

        var entry = new AppLogService(fileSystem, Paths).ReadTail().ShouldHaveSingleItem();

        entry.Level.ShouldBe(AppLogLevel.Error);
        entry.Text.ShouldBe("2026-08-13T12:30:15Z [ERROR] Version 1.22.6 : aucun exécutable attendu.");
    }
}