using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Launching;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Launching;

public sealed class RunningInstanceTrackerTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly GameVersion SampleVersion = GameVersion.Parse("1.21.3");

    private static (RunningInstanceTracker Tracker, InstanceService InstanceService, IInstanceRepository Repository, FakeClock Clock) CreateTracker()
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var clock = new FakeClock(Now);
        var instanceService = new InstanceService(repository, fileSystem, clock);

        return (new RunningInstanceTracker(instanceService, clock), instanceService, repository, clock);
    }

    private static async Task<string> CreateInstanceAsync(InstanceService instanceService)
    {
        var record = await instanceService.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        return record.Slug;
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var (_, instanceService, _, clock) = CreateTracker();

        Should.Throw<ArgumentNullException>(() => new RunningInstanceTracker(null!, clock));
        Should.Throw<ArgumentNullException>(() => new RunningInstanceTracker(instanceService, null!));
    }

    [Fact]
    public async Task IsRunning_NeverTracked_ReturnsFalse()
    {
        var (tracker, _, _, _) = CreateTracker();

        tracker.IsRunning("ghost").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TrackStartedAsync_NewProcess_MarksInstanceLaunchedAtCurrentClockTime()
    {
        var (tracker, instanceService, repository, clock) = CreateTracker();
        var slug = await CreateInstanceAsync(instanceService);
        clock.UtcNow = Now.AddHours(1);

        var status = await tracker.TrackStartedAsync(slug, new FakeRunningProcess { Id = 999 }, CancellationToken.None);

        status.State.ShouldBe(RunningInstanceState.Started);
        status.ProcessId.ShouldBe(999);
        status.StartedUtc.ShouldBe(Now.AddHours(1));
        var reloaded = await repository.LoadAsync(slug, CancellationToken.None);
        reloaded.Metadata.LastLaunchedUtc.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public async Task TrackStartedAsync_NewProcess_IsRunningBecomesTrue()
    {
        var (tracker, instanceService, _, _) = CreateTracker();
        var slug = await CreateInstanceAsync(instanceService);

        await tracker.TrackStartedAsync(slug, new FakeRunningProcess(), CancellationToken.None);

        tracker.IsRunning(slug).ShouldBeTrue();
    }

    [Fact]
    public async Task TrackStartedAsync_RaisesStatusChangedWithStartedStatus()
    {
        var (tracker, instanceService, _, _) = CreateTracker();
        var slug = await CreateInstanceAsync(instanceService);
        RunningInstanceStatus? observed = null;
        tracker.StatusChanged += (_, status) => observed = status;

        await tracker.TrackStartedAsync(slug, new FakeRunningProcess(), CancellationToken.None);

        observed.ShouldNotBeNull();
        observed!.State.ShouldBe(RunningInstanceState.Started);
        observed.Slug.ShouldBe(slug);
    }

    [Fact]
    public async Task TrackStartedAsync_AlreadyRunning_ThrowsInstanceAlreadyRunningException()
    {
        var (tracker, instanceService, _, _) = CreateTracker();
        var slug = await CreateInstanceAsync(instanceService);
        await tracker.TrackStartedAsync(slug, new FakeRunningProcess(), CancellationToken.None);

        var exception = await Should.ThrowAsync<InstanceAlreadyRunningException>(
            () => tracker.TrackStartedAsync(slug, new FakeRunningProcess(), CancellationToken.None));

        exception.Slug.ShouldBe(slug);
    }

    [Fact]
    public async Task ProcessExit_ElapsedTimeSinceStart_IsAddedAsPlaytime()
    {
        var (tracker, instanceService, repository, clock) = CreateTracker();
        var slug = await CreateInstanceAsync(instanceService);
        var process = new FakeRunningProcess();
        var stopped = await TrackAndWaitForExitAsync(tracker, slug, process, clock, advanceBy: TimeSpan.FromMinutes(30), exitCode: 0);

        stopped.State.ShouldBe(RunningInstanceState.Stopped);
        stopped.ExitCode.ShouldBe(0);
        var reloaded = await repository.LoadAsync(slug, CancellationToken.None);
        reloaded.Metadata.TotalPlaytimeSeconds.ShouldBe(1800L);
    }

    [Fact]
    public async Task ProcessExit_AccumulatesOnTopOfPreviousSessions()
    {
        var (tracker, instanceService, _, clock) = CreateTracker();
        var slug = await CreateInstanceAsync(instanceService);
        await instanceService.AddPlaytimeAsync(slug, 600, CancellationToken.None);

        await TrackAndWaitForExitAsync(tracker, slug, new FakeRunningProcess(), clock, TimeSpan.FromMinutes(10), exitCode: 0);

        var reloaded = await instanceService.AddPlaytimeAsync(slug, 0, CancellationToken.None);
        reloaded.Metadata.TotalPlaytimeSeconds.ShouldBe(1200L);
    }

    [Fact]
    public async Task ProcessExit_IsRunningBecomesFalse()
    {
        var (tracker, instanceService, _, clock) = CreateTracker();
        var slug = await CreateInstanceAsync(instanceService);

        await TrackAndWaitForExitAsync(tracker, slug, new FakeRunningProcess(), clock, TimeSpan.FromMinutes(1), exitCode: 0);

        tracker.IsRunning(slug).ShouldBeFalse();
    }

    [Fact]
    public async Task ProcessExit_NonZeroExitCode_IsReflectedInStatus()
    {
        var (tracker, instanceService, _, clock) = CreateTracker();
        var slug = await CreateInstanceAsync(instanceService);

        var stopped = await TrackAndWaitForExitAsync(tracker, slug, new FakeRunningProcess(), clock, TimeSpan.FromSeconds(5), exitCode: 134);

        stopped.ExitCode.ShouldBe(134);
    }

    [Fact]
    public async Task ProcessExit_DisposesTheRunningProcess()
    {
        var (tracker, instanceService, _, clock) = CreateTracker();
        var slug = await CreateInstanceAsync(instanceService);
        var process = new FakeRunningProcess();

        await TrackAndWaitForExitAsync(tracker, slug, process, clock, TimeSpan.FromSeconds(5), exitCode: 0);

        process.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task TrackStartedAsync_SameSlugAfterPreviousSessionExited_Succeeds()
    {
        var (tracker, instanceService, _, clock) = CreateTracker();
        var slug = await CreateInstanceAsync(instanceService);
        await TrackAndWaitForExitAsync(tracker, slug, new FakeRunningProcess(), clock, TimeSpan.FromSeconds(1), exitCode: 0);

        var status = await tracker.TrackStartedAsync(slug, new FakeRunningProcess(), CancellationToken.None);

        status.State.ShouldBe(RunningInstanceState.Started);
    }

    [Fact]
    public async Task RequestStop_RunningInstance_KillsProcessIncludingChildren()
    {
        var (tracker, instanceService, _, _) = CreateTracker();
        var slug = await CreateInstanceAsync(instanceService);
        var process = new FakeRunningProcess();
        await tracker.TrackStartedAsync(slug, process, CancellationToken.None);

        tracker.RequestStop(slug);

        process.IsKilled.ShouldBeTrue();
        process.KilledIncludingChildren.ShouldBe(true);
    }

    [Fact]
    public async Task RequestStop_NotRunning_DoesNotThrow()
    {
        var (tracker, _, _, _) = CreateTracker();

        Should.NotThrow(() => tracker.RequestStop("ghost"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RequestStop_AlreadyStopped_DoesNotKillAgain()
    {
        var (tracker, instanceService, _, clock) = CreateTracker();
        var slug = await CreateInstanceAsync(instanceService);
        var process = new FakeRunningProcess();
        await TrackAndWaitForExitAsync(tracker, slug, process, clock, TimeSpan.FromSeconds(1), exitCode: 0);

        tracker.RequestStop(slug);

        process.IsKilled.ShouldBeFalse();
    }

    [Fact]
    public async Task GetStatus_NeverTracked_ReturnsNull()
    {
        var (tracker, _, _, _) = CreateTracker();

        tracker.GetStatus("ghost").ShouldBeNull();
        await Task.CompletedTask;
    }

    // Complète le processus factice puis attend, via l'évènement StatusChanged, que
    // RunningInstanceTracker ait fini de traiter la sortie en tâche de fond (ObserveExitAsync
    // n'est pas attendue par TrackStartedAsync, volontairement : voir sa docstring). Un timeout
    // court fait échouer le test proprement plutôt que de bloquer la suite indéfiniment si le
    // comportement attendu se casse.
    private static async Task<RunningInstanceStatus> TrackAndWaitForExitAsync(
        RunningInstanceTracker tracker,
        string slug,
        FakeRunningProcess process,
        FakeClock clock,
        TimeSpan advanceBy,
        int exitCode)
    {
        var stoppedTcs = new TaskCompletionSource<RunningInstanceStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStatusChanged(object? sender, RunningInstanceStatus status)
        {
            if (status.Slug == slug && status.State == RunningInstanceState.Stopped)
            {
                stoppedTcs.TrySetResult(status);
            }
        }

        tracker.StatusChanged += OnStatusChanged;
        try
        {
            await tracker.TrackStartedAsync(slug, process, CancellationToken.None).ConfigureAwait(false);
            clock.UtcNow = clock.UtcNow.Add(advanceBy);
            process.CompleteWith(exitCode);

            return await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        finally
        {
            tracker.StatusChanged -= OnStatusChanged;
        }
    }
}