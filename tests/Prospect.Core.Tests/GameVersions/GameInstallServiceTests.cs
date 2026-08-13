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
            DownloadOptions.Default with { BufferSize = 128, ProgressInterval = TimeSpan.Zero });

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
            new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions()), fileSystem, NullAppLog.Instance);

        var installed = await service.InstallAsync(Version, cancellationToken: CancellationToken.None);

        installed.Version.ShouldBe(Version);
        repository.IsInstalled(Version).ShouldBeTrue();
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(installed.Directory, "Vintagestory")).ShouldBe("#!/bin/sh");
    }

    /// <summary>
    /// Le défaut rapporté en test réel sous Windows, mais posé au niveau du service pour qu'il vaille
    /// sur les TROIS OS : la stratégie rend la main sans erreur alors que rien n'a atterri dans le
    /// dossier de la version. Avant, c'était marqué complet. Maintenant, c'est un échec typé.
    /// </summary>
    [Theory]
    [InlineData(GamePlatforms.Windows, "Vintagestory.exe")]
    [InlineData(GamePlatforms.Linux, "Vintagestory")]
    [InlineData(GamePlatforms.MacArm64, "Vintagestory.app/Contents/MacOS/Vintagestory")]
    public async Task InstallAsync_StrategySucceedsButLeavesNoExecutable_FailsAndMarksNothing(string platformKey, string expectedName)
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var log = new RecordingAppLog();
        var strategy = new FakeGameInstallStrategy(fileSystem)
        {
            PlatformKeys = [platformKey],
            ExpectedExecutables = [GameExecutableLocation.Of(expectedName.Split('/'))],
            ProducesExecutable = false,
        };
        var service = new GameInstallService(
            new FakeGameVersionCatalog(CatalogFor(server, platformKey)), downloads, repository, strategy, fileSystem, log);

        var exception = await Should.ThrowAsync<GameInstallIncompleteException>(
            () => service.InstallAsync(Version, cancellationToken: CancellationToken.None));

        exception.TargetDirectory.ShouldBe(repository.GetVersionDirectory(Version));
        exception.ExpectedExecutables.ShouldBe([expectedName]);
        repository.IsInstalled(Version).ShouldBeFalse();
        fileSystem.Directory.Exists(repository.GetVersionDirectory(Version)).ShouldBeFalse();
        log.Lines.ShouldContain(line => line.Level == AppLogLevel.Error);
    }

    /// <summary>
    /// Le pendant positif : la vérification s'accommode d'un exécutable de REPLI, et le journal en
    /// garde la trace pour que le prochain rapport de terrain soit lisible.
    /// </summary>
    [Fact]
    public async Task InstallAsync_ExecutableFoundThroughTheFallbackName_StillCountsAsComplete()
    {
        var server = new FakeDownloadServer(TarGzSamples.Create(("Vintagestory.exe", TarGzSamples.Text("MZ"))));
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var log = new RecordingAppLog();
        var service = new GameInstallService(
            new FakeGameVersionCatalog(CatalogFor(server)),
            downloads,
            repository,
            new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions()), fileSystem, log);

        await service.InstallAsync(Version, cancellationToken: CancellationToken.None);

        repository.IsInstalled(Version).ShouldBeTrue();
        log.Lines.ShouldContain(line => line.Level == AppLogLevel.Info && line.Message.Contains("Vintagestory.exe", StringComparison.Ordinal));
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
        var strategy = new FakeGameInstallStrategy(fileSystem)
        {
            BeforeReturning = _ => markerDuringInstall = repository.IsInstalled(Version),
        };
        var service = new GameInstallService(new FakeGameVersionCatalog(CatalogFor(server)), downloads, repository, strategy, fileSystem, NullAppLog.Instance);

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
            new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions()), fileSystem, NullAppLog.Instance);

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
            new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions()), fileSystem, NullAppLog.Instance);

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
    public async Task InstallAsync_WindowsInstallerThatWritesNothing_KeepsTheInstallPhaseIndeterminate()
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var reports = new List<GameInstallProgress>();
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);

        // L'installeur factice pose l'exécutable, comme le vrai : sans ça, c'est la vérification
        // post-installation qui parlerait, pas la progression que ce test observe.
        var runner = new FakeProcessRunner
        {
            OnRun = request => fileSystem.AddFile(
                fileSystem.Path.Combine(repository.GetVersionDirectory(Version), "Vintagestory.exe"),
                new MockFileData("MZ")),
        };
        var service = new GameInstallService(
            new FakeGameVersionCatalog(CatalogFor(server, GamePlatforms.Windows)),
            downloads,
            repository,
            new WindowsGameInstallStrategy(fileSystem, runner, NullAppLog.Instance), fileSystem, NullAppLog.Instance);

        await service.InstallAsync(
            Version,
            new SynchronousProgress<GameInstallProgress>(reports.Add),
            CancellationToken.None);

        // Le processus factice rend la main tout de suite : l'observateur du dossier n'a pas eu un
        // seul battement pour échantillonner, donc la phase reste franchement indéterminée. C'est
        // le repli, et il est intact.
        var installing = reports.Where(report => report.Phase == GameInstallPhase.Installing).ToArray();
        installing.ShouldHaveSingleItem().Ratio.ShouldBeNull();
    }

    /// <summary>
    /// L'installeur Inno silencieux ne publie rien, mais il écrit ses fichiers progressivement dans
    /// le dossier <c>/DIR</c>. L'avancement de la phase d'installation vient de là, et il est marqué
    /// comme ESTIMÉ pour que l'interface l'étiquette au lieu de le présenter comme un décompte.
    /// </summary>
    [Fact]
    public async Task InstallAsync_WindowsInstaller_EstimatesItsProgressFromTheTargetFolderGrowing()
    {
        var server = new FakeDownloadServer(SampleArchive());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var downloads = CreateDownloads(handler, fileSystem);
        var reports = new List<GameInstallProgress>();
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var target = repository.GetVersionDirectory(Version);

        // Un battement autorisé par Release() : le test contrôle exactement quand un échantillon
        // est pris, plutôt que d'attendre de vraies secondes.
        using var beat = new SemaphoreSlim(0);
        var runner = new FakeProcessRunner
        {
            OnRunAsync = async (_, _) =>
            {
                fileSystem.AddFile(fileSystem.Path.Combine(target, "assets.dat"), new MockFileData(new byte[200]));
                beat.Release();
                await WaitForAsync(() => reports.Any(report => report.IsEstimated));

                fileSystem.AddFile(fileSystem.Path.Combine(target, "Vintagestory.exe"), new MockFileData(new byte[400]));
                beat.Release();
                await WaitForAsync(() => reports.Count(report => report.IsEstimated) >= 2);
            },
        };

        var service = new GameInstallService(
            new FakeGameVersionCatalog(CatalogFor(server, GamePlatforms.Windows)),
            downloads,
            repository,
            new WindowsGameInstallStrategy(fileSystem, runner, NullAppLog.Instance, delay: (_, token) => beat.WaitAsync(token)),
            fileSystem,
            NullAppLog.Instance);

        await service.InstallAsync(
            Version,
            new SynchronousProgress<GameInstallProgress>(reports.Add),
            CancellationToken.None);

        var estimated = reports.Where(report => report.IsEstimated).ToArray();
        estimated.Length.ShouldBeGreaterThanOrEqualTo(2);
        estimated.ShouldAllBe(report => report.Phase == GameInstallPhase.Installing);

        var ratios = estimated.Select(report => report.Ratio!.Value).ToArray();
        ratios.ShouldBe(ratios.Order().ToArray());
        ratios.ShouldAllBe(ratio => ratio <= InstallDirectoryGrowthReporter.MaxReportedRatio);

        // Les compteurs d'octets restent ceux du téléchargement, jamais réutilisés pour l'installation.
        estimated.ShouldAllBe(report => report.ReceivedBytes == 0L);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(2);
        }

        condition().ShouldBeTrue("La condition attendue n'est jamais survenue.");
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
            new FakeGameInstallStrategy(fileSystem), fileSystem, NullAppLog.Instance);

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
            new MacOsGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions()), fileSystem, NullAppLog.Instance);

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
        var strategy = new FakeGameInstallStrategy(fileSystem)
        {
            Failure = new GameInstallFailedException("l'installeur a rendu l'âme"),
            BeforeReturning = target => fileSystem.AddFile(fileSystem.Path.Combine(target, "moitie.bin"), new MockFileData("x")),
        };
        var service = new GameInstallService(new FakeGameVersionCatalog(CatalogFor(server)), downloads, repository, strategy, fileSystem, NullAppLog.Instance);

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
        var strategy = new FakeGameInstallStrategy(fileSystem);
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
        var service = new GameInstallService(new FakeGameVersionCatalog(tampered), downloads, repository, strategy, fileSystem, NullAppLog.Instance);

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
            new FakeGameInstallStrategy(fileSystem), fileSystem, NullAppLog.Instance);

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
            new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions()), fileSystem, NullAppLog.Instance);

        await service.InstallAsync(Version, cancellationToken: CancellationToken.None);

        fileSystem.File.Exists(fileSystem.Path.Combine(repository.GetVersionDirectory(Version), "reliquat.bin")).ShouldBeFalse();
        repository.IsInstalled(Version).ShouldBeTrue();
    }

    [Fact]
    public async Task Uninstall_RemovesTheInstalledFolderAndCountsItsFiles()
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
            new FakeGameInstallStrategy(fileSystem), fileSystem, NullAppLog.Instance);

        var reports = new List<DirectoryDeleteProgress>();
        await service.UninstallAsync(Version, new SynchronousProgress<DirectoryDeleteProgress>(reports.Add), CancellationToken.None);

        repository.IsInstalled(Version).ShouldBeFalse();

        // L'avancement est DÉTERMINÉ : les fichiers sont comptés avant d'être effacés.
        reports.ShouldNotBeEmpty();
        reports[^1].Ratio.ShouldBe(1d);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fileSystem = new MockFileSystem();
        var catalog = new FakeGameVersionCatalog(new GameVersionCatalog([], Noon, GameCatalogFreshness.Live));
        var downloads = new FakeDownloadManagerStub();
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var strategy = new FakeGameInstallStrategy(fileSystem);

        Should.Throw<ArgumentNullException>(() => new GameInstallService(null!, downloads, repository, strategy, fileSystem, NullAppLog.Instance));
        Should.Throw<ArgumentNullException>(() => new GameInstallService(catalog, null!, repository, strategy, fileSystem, NullAppLog.Instance));
        Should.Throw<ArgumentNullException>(() => new GameInstallService(catalog, downloads, null!, strategy, fileSystem, NullAppLog.Instance));
        Should.Throw<ArgumentNullException>(() => new GameInstallService(catalog, downloads, repository, null!, fileSystem, NullAppLog.Instance));
        Should.Throw<ArgumentNullException>(() => new GameInstallService(catalog, downloads, repository, strategy, null!, NullAppLog.Instance));
        Should.Throw<ArgumentNullException>(() => new GameInstallService(catalog, downloads, repository, strategy, fileSystem, null!));
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

        public void DismissFinished()
        {
        }
    }
}