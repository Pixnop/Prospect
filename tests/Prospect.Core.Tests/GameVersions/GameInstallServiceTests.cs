using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Http;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Http;
using Prospect.Core.Tests.Instances;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.GameVersions;

public sealed class GameInstallServiceTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameVersion Version = GameVersion.Parse("1.22.6");
    private static readonly Uri CdnUrl = new("https://cdn.example/vs_client_linux-x64_1.22.6.tar.gz");
    private static readonly Uri LocalUrl = new("https://local.example/vs_client_linux-x64_1.22.6.tar.gz");

    private static byte[] SampleArchive() => TarGzSamples.Create(
        ("Vintagestory", TarGzSamples.Text("#!/bin/sh")),
        ("assets/game/lang/fr.json", TarGzSamples.Text("{}")));

    private static GameVersionCatalog CatalogFor(FakeDownloadServer server, string platformKey = GamePlatforms.Linux)
    {
        var asset = new GameVersionAsset(
            platformKey,
            "vs_client_linux-x64_1.22.6.tar.gz",
            "590.5 MB",
            server.Md5,
            CdnUrl,
            LocalUrl,
            IsLatest: true);

        var entry = new GameVersionCatalogEntry(Version, new Dictionary<string, GameVersionAsset>(StringComparer.Ordinal)
        {
            [platformKey] = asset,
        });

        return new GameVersionCatalog([entry], Noon, GameCatalogFreshness.Live);
    }

    private static DownloadManager CreateDownloads(FakeHttpMessageHandler handler, MockFileSystem fileSystem)
        => new(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            fileSystem,
            Paths,
            new FakeClock(Noon),
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask),
            DownloadOptions.Default with { BufferSize = 128, ProgressStepBytes = 128 });

    [Fact]
    public async Task InstallAsync_HappyPath_ExtractsTheArchiveIntoTheVersionFolder()
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var service = new GameInstallService(
            new FakeGameVersionCatalog(CatalogFor(server)),
            downloads,
            repository,
            new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions()));

        var installed = await service.InstallAsync(Version, cancellationToken: CancellationToken.None);

        installed.Version.ShouldBe(Version);
        repository.IsInstalled(Version).ShouldBeTrue();
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(installed.Directory, "Vintagestory")).ShouldBe("#!/bin/sh");
    }

    [Fact]
    public async Task InstallAsync_WritesTheCompletionMarkerOnlyAfterTheStrategyIsDone()
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var markerDuringInstall = true;
        var strategy = new FakeGameInstallStrategy
        {
            BeforeReturning = _ => markerDuringInstall = repository.IsInstalled(Version),
        };
        var service = new GameInstallService(new FakeGameVersionCatalog(CatalogFor(server)), downloads, repository, strategy);

        await service.InstallAsync(Version, cancellationToken: CancellationToken.None);

        markerDuringInstall.ShouldBeFalse();
        repository.IsInstalled(Version).ShouldBeTrue();
    }

    [Fact]
    public async Task InstallAsync_ReportsTheFourPhasesInOrder()
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var phases = new List<GameInstallPhase>();
        var service = new GameInstallService(
            new FakeGameVersionCatalog(CatalogFor(server)),
            downloads,
            new FileSystemInstalledGameVersionRepository(fileSystem, Paths),
            new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions()));

        await service.InstallAsync(
            Version,
            new SynchronousProgress<GameInstallProgress>(report => phases.Add(report.Phase)),
            CancellationToken.None);

        phases.ShouldContain(GameInstallPhase.Downloading);
        phases.ShouldContain(GameInstallPhase.Verifying);
        phases.ShouldContain(GameInstallPhase.Installing);
        phases[^1].ShouldBe(GameInstallPhase.Completed);
        phases.IndexOf(GameInstallPhase.Verifying).ShouldBeGreaterThan(phases.IndexOf(GameInstallPhase.Downloading));
        phases.IndexOf(GameInstallPhase.Installing).ShouldBeGreaterThan(phases.IndexOf(GameInstallPhase.Verifying));
    }

    /// <summary>
    /// La phase d'installation n'annonçait qu'UNE chose, et avant d'avoir commencé : « Installation »,
    /// sans avancement, puis plus rien jusqu'à la fin. Sur plusieurs centaines de mégaoctets à
    /// extraire, la barre restait figée sans que rien ne distingue le travail d'un blocage. La
    /// stratégie qui sait se mesurer publie désormais un avancement chiffré, croissant, borné à 1.
    /// </summary>
    [Fact]
    public async Task InstallAsync_TarGzStrategy_ReportsAMeasuredRatioWhileExtracting()
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var reports = new List<GameInstallProgress>();
        var service = new GameInstallService(
            new FakeGameVersionCatalog(CatalogFor(server)),
            downloads,
            new FileSystemInstalledGameVersionRepository(fileSystem, Paths),
            new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions()));

        await service.InstallAsync(
            Version,
            new SynchronousProgress<GameInstallProgress>(reports.Add),
            CancellationToken.None);

        var installing = reports.Where(report => report.Phase == GameInstallPhase.Installing).ToArray();

        // Le premier rapport de la phase reste indéterminé (le service annonce l'entrée en phase),
        // puis la stratégie prend le relais avec des rapports chiffrés.
        installing.Length.ShouldBeGreaterThan(1);
        installing[0].Ratio.ShouldBeNull();

        var measured = installing.Skip(1).ToArray();
        measured.ShouldNotBeEmpty("l'extraction doit publier son avancement, pas seulement son début");
        measured.ShouldAllBe(report => report.Ratio != null && report.Ratio >= 0d && report.Ratio <= 1d);

        // Monotone, et terminée à 1 : la barre ne recule jamais et ne reste pas coincée avant la fin.
        var ratios = measured.Select(report => report.Ratio!.Value).ToArray();
        ratios.ShouldBe(ratios.Order().ToArray());
        ratios[^1].ShouldBe(1d);

        // Les compteurs d'octets restent ceux du téléchargement : la phase d'installation mesure
        // une progression, pas des octets écrits, et ne prétend pas le contraire.
        measured.ShouldAllBe(report => report.ReceivedBytes == 0L && report.TotalBytes == null);
    }

    /// <summary>
    /// L'installeur Inno silencieux ne rend la main qu'une fois terminé : rien à chiffrer, et c'est
    /// un fait de l'outil. La phase doit donc rester proprement indéterminée plutôt que d'inventer
    /// un pourcentage — l'interface sait afficher une barre indéterminée.
    /// </summary>
    [Fact]
    public async Task InstallAsync_WindowsInstaller_KeepsTheInstallPhaseHonestlyIndeterminate()
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var reports = new List<GameInstallProgress>();
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var service = new GameInstallService(
            new FakeGameVersionCatalog(CatalogFor(server, GamePlatforms.Windows)),
            downloads,
            repository,
            new WindowsGameInstallStrategy(fileSystem, new FakeProcessRunner()));

        await service.InstallAsync(
            Version,
            new SynchronousProgress<GameInstallProgress>(reports.Add),
            CancellationToken.None);

        var installing = reports.Where(report => report.Phase == GameInstallPhase.Installing).ToArray();
        installing.ShouldHaveSingleItem().Ratio.ShouldBeNull();
    }

    [Fact]
    public async Task InstallAsync_VersionAbsentFromTheCatalog_ThrowsTheTypedFailure()
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var service = new GameInstallService(
            new FakeGameVersionCatalog(new GameVersionCatalog([], Noon, GameCatalogFreshness.Live)),
            downloads,
            new FileSystemInstalledGameVersionRepository(fileSystem, Paths),
            new FakeGameInstallStrategy());

        var exception = await Should.ThrowAsync<GameVersionNotAvailableException>(
            () => service.InstallAsync(Version, cancellationToken: CancellationToken.None));

        exception.Message.ShouldContain("1.22.6");
    }

    [Fact]
    public async Task InstallAsync_NoBuildForThisPlatform_ThrowsTheTypedFailure()
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var service = new GameInstallService(
            new FakeGameVersionCatalog(CatalogFor(server, GamePlatforms.Windows)),
            downloads,
            new FileSystemInstalledGameVersionRepository(fileSystem, Paths),
            new MacOsGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions()));

        var exception = await Should.ThrowAsync<GameVersionNotAvailableException>(
            () => service.InstallAsync(Version, cancellationToken: CancellationToken.None));

        exception.Message.ShouldContain(GamePlatforms.MacArm64);
    }

    [Fact]
    public async Task InstallAsync_StrategyFails_LeavesNoHalfInstalledFolderBehind()
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var strategy = new FakeGameInstallStrategy
        {
            Failure = new GameInstallFailedException("l'installeur a rendu l'âme"),
            BeforeReturning = target => fileSystem.AddFile(fileSystem.Path.Combine(target, "moitie.bin"), new MockFileData("x")),
        };
        var service = new GameInstallService(new FakeGameVersionCatalog(CatalogFor(server)), downloads, repository, strategy);

        await Should.ThrowAsync<GameInstallFailedException>(
            () => service.InstallAsync(Version, cancellationToken: CancellationToken.None));

        repository.IsInstalled(Version).ShouldBeFalse();
        fileSystem.Directory.Exists(repository.GetVersionDirectory(Version)).ShouldBeFalse();
    }

    [Fact]
    public async Task InstallAsync_ChecksumMismatch_StopsBeforeInstallingAnything()
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var strategy = new FakeGameInstallStrategy();
        var catalog = CatalogFor(server);
        var tampered = new GameVersionCatalog(
            [
                new GameVersionCatalogEntry(Version, new Dictionary<string, GameVersionAsset>(StringComparer.Ordinal)
                {
                    [GamePlatforms.Linux] = catalog.Versions[0].Assets[GamePlatforms.Linux] with { Md5 = "00000000000000000000000000000000" },
                }),
            ],
            Noon,
            GameCatalogFreshness.Live);
        var service = new GameInstallService(new FakeGameVersionCatalog(tampered), downloads, repository, strategy);

        await Should.ThrowAsync<DownloadChecksumMismatchException>(
            () => service.InstallAsync(Version, cancellationToken: CancellationToken.None));

        strategy.Installs.ShouldBeEmpty();
        repository.IsInstalled(Version).ShouldBeFalse();
    }

    [Fact]
    public async Task InstallAsync_CanceledDuringDownload_LeavesNothingOnDisk()
    {
        using var cancellation = new CancellationTokenSource();
        var server = new FakeDownloadServer(SampleArchive()) { ChunkSize = 16 };
        server.AfterChunk = sent =>
        {
            if (sent >= 16)
            {
                cancellation.Cancel();
            }
        };
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var service = new GameInstallService(
            new FakeGameVersionCatalog(CatalogFor(server)),
            downloads,
            repository,
            new FakeGameInstallStrategy());

        await Should.ThrowAsync<OperationCanceledException>(() => service.InstallAsync(Version, cancellationToken: cancellation.Token));

        repository.IsInstalled(Version).ShouldBeFalse();
        fileSystem.Directory.Exists(repository.GetVersionDirectory(Version)).ShouldBeFalse();
        fileSystem.File.Exists(fileSystem.Path.Combine(Paths.DownloadsCacheDirectory, "vs_client_linux-x64_1.22.6.tar.gz.partial")).ShouldBeFalse();
    }

    [Fact]
    public async Task InstallAsync_LeftoverFromAnInterruptedInstall_IsWipedBeforeReinstalling()
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        fileSystem.AddFile(
            fileSystem.Path.Combine(repository.GetVersionDirectory(Version), "reliquat.bin"),
            new MockFileData("vieux morceau"));
        var service = new GameInstallService(
            new FakeGameVersionCatalog(CatalogFor(server)),
            downloads,
            repository,
            new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions()));

        await service.InstallAsync(Version, cancellationToken: CancellationToken.None);

        fileSystem.File.Exists(fileSystem.Path.Combine(repository.GetVersionDirectory(Version), "reliquat.bin")).ShouldBeFalse();
        repository.IsInstalled(Version).ShouldBeTrue();
    }

    [Fact]
    public void Uninstall_RemovesTheInstalledFolder()
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        fileSystem.AddFile(
            fileSystem.Path.Combine(repository.GetVersionDirectory(Version), FileSystemInstalledGameVersionRepository.CompletionMarkerFileName),
            new MockFileData("1.22.6"));
        var service = new GameInstallService(
            new FakeGameVersionCatalog(new GameVersionCatalog([], Noon, GameCatalogFreshness.Live)),
            new FakeDownloadManagerStub(),
            repository,
            new FakeGameInstallStrategy());

        service.Uninstall(Version);

        repository.IsInstalled(Version).ShouldBeFalse();
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fileSystem = new MockFileSystem();
        var catalog = new FakeGameVersionCatalog(new GameVersionCatalog([], Noon, GameCatalogFreshness.Live));
        var downloads = new FakeDownloadManagerStub();
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var strategy = new FakeGameInstallStrategy();

        Should.Throw<ArgumentNullException>(() => new GameInstallService(null!, downloads, repository, strategy));
        Should.Throw<ArgumentNullException>(() => new GameInstallService(catalog, null!, repository, strategy));
        Should.Throw<ArgumentNullException>(() => new GameInstallService(catalog, downloads, null!, strategy));
        Should.Throw<ArgumentNullException>(() => new GameInstallService(catalog, downloads, repository, null!));
    }

    private sealed class FakeDownloadManagerStub : IDownloadManager
    {
        public IReadOnlyList<DownloadOperation> Operations => [];

        public event EventHandler? OperationsChanged;

        public Task<string> DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            OperationsChanged?.Invoke(this, EventArgs.Empty);

            return Task.FromResult(request.FileName);
        }

        public void Dismiss(DownloadOperation operation)
        {
        }
    }
}