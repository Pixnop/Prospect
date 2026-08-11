using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Launching;
using Prospect.Core.Runtime;
using Prospect.Core.Storage;
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
        RecordingOverlayService Overlay);

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
            repository, versions, dotnetLocator, tracker, new LinuxGameLaunchStrategy(fileSystem), processRunner, fileSystem, Paths, clock);
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();

        var card = new InstanceCardViewModel(record, "jamais", service, launcher, tracker, overlay, toasts, new ImmediateUiDispatcher(), () => Task.CompletedTask);

        return new Fixture(card, tracker, processRunner, dotnetLocator, toasts, overlay);
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
    public async Task PlayCommand_RuntimeMissing_ShowsErrorToastNamingTheRuntime()
    {
        var fixture = Create();
        fixture.DotnetLocator.Result = RuntimeCheckResult.Missing(GameRuntimeRequirement.Known("Microsoft.NETCore.App", new Version(8, 0, 10)));

        await fixture.Card.PlayCommand.ExecuteAsync(null);

        fixture.Card.IsRunning.ShouldBeFalse();
        var toast = fixture.Toasts.Shown.ShouldHaveSingleItem();
        toast.Title.ShouldBe("Runtime .NET manquant");
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
            repository, versions, new FakeDotnetLocator(), tracker, new LinuxGameLaunchStrategy(fileSystem), new FakeProcessRunner(), fileSystem, Paths, clock);
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();
        var dispatcher = new ImmediateUiDispatcher();
        Func<Task> requestRefresh = () => Task.CompletedTask;

        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(null!, "jamais", service, launcher, tracker, overlay, toasts, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", null!, launcher, tracker, overlay, toasts, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, null!, tracker, overlay, toasts, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, launcher, null!, overlay, toasts, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, launcher, tracker, null!, toasts, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, launcher, tracker, overlay, null!, dispatcher, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, launcher, tracker, overlay, toasts, null!, requestRefresh));
        Should.Throw<ArgumentNullException>(() => new InstanceCardViewModel(record, "jamais", service, launcher, tracker, overlay, toasts, dispatcher, null!));
    }
}