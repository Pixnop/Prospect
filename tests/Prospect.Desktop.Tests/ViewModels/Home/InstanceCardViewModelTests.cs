using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Backups;
using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Launching;
using Prospect.Core.ModDb;
using Prospect.Core.Runtime;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Toasts;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Home;

/// <summary>
/// États Jouer/lancement/en cours/erreur d'<see cref="InstanceCardViewModel"/>. Mêmes
/// collaborateurs réels que <see cref="Prospect.Desktop.CompositionRoot"/> (repositories sur
/// <see cref="MockFileSystem"/>, <see cref="GameLauncher"/>/<see cref="RunningInstanceTracker"/>
/// réels), seuls <see cref="Prospect.Core.Common.IProcessRunner"/> et
/// <see cref="Prospect.Core.Runtime.IDotnetLocator"/> sont des doubles.
/// </summary>
public sealed class InstanceCardViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly GameVersion SampleVersion = GameVersion.Parse("1.21.3");

    private sealed record Fixture(
        InstanceCardViewModel Card,
        RunningInstanceTracker Tracker,
        FakeProcessRunner ProcessRunner,
        FakeDotnetLocator DotnetLocator,
        RecordingToastService Toasts,
        RecordingOverlayService Overlay,
        IModUpdateCheckCache UpdateCache,
        string Slug);

    private static Fixture Create(bool installVersion = true)
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var clock = new FakeClock(Now);
        var service = new InstanceService(repository, fileSystem, clock);
        var record = service.CreateAsync("Homestead", SampleVersion).GetAwaiter().GetResult();

        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        if (installVersion)
        {
            versions.PrepareDirectory(SampleVersion);
            versions.MarkCompleteAsync(SampleVersion, CancellationToken.None).GetAwaiter().GetResult();
        }

        var processRunner = new FakeProcessRunner();
        var dotnetLocator = new FakeDotnetLocator();
        var tracker = new RunningInstanceTracker(service, clock);
        var launcher = new GameLauncher(
            repository, versions, dotnetLocator, tracker, new LinuxGameLaunchStrategy(fileSystem), processRunner, fileSystem, Paths, clock,
            AccountDoubles.SignedOut(), AccountDoubles.ClientSettings(fileSystem), new InstanceBackupService(repository, fileSystem, clock));
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();
        var updateCache = new ModUpdateCheckCache();

        var card = new InstanceCardViewModel(
            record, "jamais", service, launcher, tracker, updateCache, overlay, toasts, new ImmediateUiDispatcher(), () => Task.CompletedTask);

        return new Fixture(card, tracker, processRunner, dotnetLocator, toasts, overlay, updateCache, record.Slug);
    }

    [Fact]
    public async Task PlayCommand_VersionInstalledAndRuntimePresent_BecomesRunning()
    {
        var fixture = Create();

        await fixture.Card.PlayCommand.ExecuteAsync(null);

        fixture.Card.IsRunning.ShouldBeTrue();
        fixture.Card.IsLaunching.ShouldBeFalse();
    }

    [Fact]
    public async Task PlayCommand_VersionNotInstalled_ShowsErrorToastAndStaysNotRunning()
    {
        var fixture = Create(installVersion: false);

        await fixture.Card.PlayCommand.ExecuteAsync(null);

        fixture.Card.IsRunning.ShouldBeFalse();
        var toast = fixture.Toasts.Shown.ShouldHaveSingleItem();
        toast.Tone.ShouldBe(ToastTone.Error);
        toast.Title.ShouldBe("Version non installée");
    }

    [Fact]
    public async Task PlayCommand_AutoBackupEnabledAndFails_ShowsAProminentWarningToastButStillLaunches()
    {
        // Même garantie que côté page de détail (InstanceDetailViewModelTests) : les deux
        // surfaces de lancement doivent traiter l'échec du filet de sécurité de façon identique.
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var clock = new FakeClock(Now);
        var service = new InstanceService(repository, fileSystem, clock);
        var record = await service.CreateAsync("Homestead", SampleVersion);
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        versions.PrepareDirectory(SampleVersion);
        await versions.MarkCompleteAsync(SampleVersion, CancellationToken.None);
        var tracker = new RunningInstanceTracker(service, clock);
        var backups = new InstanceBackupService(repository, fileSystem, clock);
        var launcher = new GameLauncher(
            repository, versions, new FakeDotnetLocator(), tracker, new LinuxGameLaunchStrategy(fileSystem), new FakeProcessRunner(), fileSystem, Paths, clock,
            AccountDoubles.SignedOut(), AccountDoubles.ClientSettings(fileSystem), backups);
        var toasts = new RecordingToastService();
        var card = new InstanceCardViewModel(
            record, "jamais", service, launcher, tracker, new ModUpdateCheckCache(),
            new RecordingOverlayService(), toasts, new ImmediateUiDispatcher(), () => Task.CompletedTask);
        await service.UpdateBackupSettingsAsync(record.Slug, new InstanceBackupSettings { AutoBeforeLaunch = true }, CancellationToken.None);
        fileSystem.AddFile(backups.GetBackupsDirectory(record.Slug), new MockFileData("obstacle"));

        await card.PlayCommand.ExecuteAsync(null);

        card.IsRunning.ShouldBeTrue();
        toasts.Shown.Single(t => t.Title == "Sauvegarde automatique ratée").Tone.ShouldBe(ToastTone.Warning);
    }

    [Fact]
    public async Task PlayCommand_RuntimeMissing_ShowsErrorToastNamingTheRuntime()
    {
        var fixture = Create();
        fixture.DotnetLocator.Result = RuntimeCheckResult.Missing(GameRuntimeRequirement.Known("Microsoft.NETCore.App", new Version(8, 0, 10)));

        await fixture.Card.PlayCommand.ExecuteAsync(null);

        fixture.Card.IsRunning.ShouldBeFalse();
        var toast = fixture.Toasts.Shown.ShouldHaveSingleItem();
        toast.Title.ShouldBe("Composant .NET manquant");
        toast.Description.ShouldNotBeNull().ShouldContain("8.0.10");
    }

    [Fact]
    public async Task PlayCommand_AlreadyRunning_CannotExecuteAgain()
    {
        var fixture = Create();
        await fixture.Card.PlayCommand.ExecuteAsync(null);

        fixture.Card.PlayCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Open_RaisesOpenRequestedWithSlug()
    {
        var fixture = Create();
        string? openedSlug = null;
        fixture.Card.OpenRequested += (_, slug) => openedSlug = slug;

        fixture.Card.OpenCommand.Execute(null);

        openedSlug.ShouldBe(fixture.Card.Slug);
    }

    [Fact]
    public async Task RequestStop_WhileRunning_OpensConfirmationDialog()
    {
        var fixture = Create();
        await fixture.Card.PlayCommand.ExecuteAsync(null);

        fixture.Card.RequestStopCommand.Execute(null);

        fixture.Overlay.Active.ShouldBeOfType<StopInstanceDialogViewModel>();
    }

    [Fact]
    public async Task RequestStop_NotRunning_CannotExecute()
    {
        var fixture = Create();

        fixture.Card.RequestStopCommand.CanExecute(null).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RequestStop_ConfirmedFromDialog_KillsTheProcess()
    {
        var fixture = Create();
        var process = new FakeRunningProcess();
        fixture.ProcessRunner.NextProcessFactory = _ => process;
        await fixture.Card.PlayCommand.ExecuteAsync(null);

        fixture.Card.RequestStopCommand.Execute(null);
        var dialog = fixture.Overlay.Active.ShouldBeOfType<StopInstanceDialogViewModel>();
        dialog.ConfirmCommand.Execute(null);

        process.IsKilled.ShouldBeTrue();
    }

    [Fact]
    public async Task Dispose_TrackerStatusChangesNoLongerUpdateIsRunning()
    {
        var fixture = Create();
        fixture.Card.Dispose();

        await fixture.Card.PlayCommand.ExecuteAsync(null);

        // PlayCommand appelle GameLauncher directement (pas affecté par Dispose), mais le tracker
        // ne notifie plus CETTE carte : IsRunning reste à sa valeur par défaut malgré le
        // lancement réel, ce qui prouve le désabonnement plutôt qu'un comportement accidentel.
        fixture.Card.IsRunning.ShouldBeFalse();
    }

    // ── Pastille « N mises à jour » (feature 4b) ─────────────────────────────────────

    [Fact]
    public void HasUpdates_NoCheckPerformedThisSession_IsFalse()
    {
        var fixture = Create();

        fixture.Card.HasUpdates.ShouldBeFalse();
        fixture.Card.UpdateCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateCount_CacheAlreadyKnowsAResultAtConstruction_IsReadImmediately()
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var clock = new FakeClock(Now);
        var service = new InstanceService(repository, fileSystem, clock);
        var record = await service.CreateAsync("Homestead", SampleVersion);
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var tracker = new RunningInstanceTracker(service, clock);
        var launcher = new GameLauncher(
            repository, versions, new FakeDotnetLocator(), tracker, new LinuxGameLaunchStrategy(fileSystem), new FakeProcessRunner(), fileSystem, Paths, clock,
            AccountDoubles.SignedOut(), AccountDoubles.ClientSettings(fileSystem), new InstanceBackupService(repository, fileSystem, clock));
        var updateCache = new ModUpdateCheckCache();
        updateCache.Store(record.Slug, SampleReport(2));

        var card = new InstanceCardViewModel(
            record, "jamais", service, launcher, tracker, updateCache,
            new RecordingOverlayService(), new RecordingToastService(), new ImmediateUiDispatcher(), () => Task.CompletedTask);

        card.UpdateCount.ShouldBe(2);
        card.HasUpdates.ShouldBeTrue();
    }

    [Fact]
    public void UpdateCount_SharedCacheStoresANewReportForThisInstance_ReflectsItLiveWithoutRebuildingTheCard()
    {
        var fixture = Create();

        fixture.UpdateCache.Store(fixture.Slug, SampleReport(3));

        fixture.Card.UpdateCount.ShouldBe(3);
        fixture.Card.HasUpdates.ShouldBeTrue();
    }

    [Fact]
    public void UpdateCount_SharedCacheStoresAReportForAnotherInstance_IsIgnored()
    {
        var fixture = Create();

        fixture.UpdateCache.Store("some-other-instance", SampleReport(5));

        fixture.Card.UpdateCount.ShouldBe(0);
    }

    [Fact]
    public void UpdateCount_SharedCacheInvalidated_GoesBackToZero()
    {
        var fixture = Create();
        fixture.UpdateCache.Store(fixture.Slug, SampleReport(4));

        fixture.UpdateCache.Invalidate(fixture.Slug);

        fixture.Card.UpdateCount.ShouldBe(0);
        fixture.Card.HasUpdates.ShouldBeFalse();
    }

    [Fact]
    public void Dispose_SharedCacheChangesNoLongerUpdateTheCard()
    {
        var fixture = Create();
        fixture.Card.Dispose();

        fixture.UpdateCache.Store(fixture.Slug, SampleReport(1));

        fixture.Card.UpdateCount.ShouldBe(0);
    }

    private static InstanceUpdateReport SampleReport(int updateCount)
    {
        var results = Enumerable.Range(0, updateCount).Select(index => new ModUpdateResult(
            new InstalledMod { FilePath = $"/mod-{index}.zip", FileName = $"mod-{index}.zip", IsEnabled = true },
            ModUpdateStatus.UpdateAvailable));

        return new InstanceUpdateReport(results.ToArray(), Now);
    }

    [Fact]
    public async Task Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var clock = new FakeClock(Now);
        var service = new InstanceService(repository, fileSystem, clock);
        var record = await service.CreateAsync("Homestead", SampleVersion);
        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var tracker = new RunningInstanceTracker(service, clock);
        var launcher = new GameLauncher(
            repository, versions, new FakeDotnetLocator(), tracker, new LinuxGameLaunchStrategy(fileSystem), new FakeProcessRunner(), fileSystem, Paths, clock,
            AccountDoubles.SignedOut(), AccountDoubles.ClientSettings(fileSystem), new InstanceBackupService(repository, fileSystem, clock));
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();
        var dispatcher = new ImmediateUiDispatcher();
        var updateCache = new ModUpdateCheckCache();
        Func<Task> requestRefresh = () => Task.CompletedTask;

        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(null!, "jamais", service, launcher, tracker, updateCache, overlay, toasts, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", null!, launcher, tracker, updateCache, overlay, toasts, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, null!, tracker, updateCache, overlay, toasts, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, launcher, null!, updateCache, overlay, toasts, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, launcher, tracker, null!, overlay, toasts, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, launcher, tracker, updateCache, null!, toasts, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, launcher, tracker, updateCache, overlay, null!, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, launcher, tracker, updateCache, overlay, toasts, null!, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, launcher, tracker, updateCache, overlay, toasts, dispatcher, null!));
    }
}