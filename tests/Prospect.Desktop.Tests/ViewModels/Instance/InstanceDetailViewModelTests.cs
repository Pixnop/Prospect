using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Backups;
using Prospect.Core.Common;
using Prospect.Core.Diagnostics;
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
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Toasts;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Instance;

/// <summary>
/// États Jouer/lancement/en cours/erreur et bascule d'onglets d'<see cref="InstanceDetailViewModel"/>.
/// Mêmes collaborateurs réels que <see cref="Prospect.Desktop.CompositionRoot"/>, seuls
/// <see cref="Prospect.Core.Common.IProcessRunner"/> et <see cref="Prospect.Core.Runtime.IDotnetLocator"/>
/// sont des doubles (voir <see cref="InstanceCardViewModelTests"/> pour le même principe côté carte).
/// </summary>
public sealed class InstanceDetailViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly GameVersion SampleVersion = GameVersion.Parse("1.21.3");

    private sealed record Fixture(
        InstanceDetailViewModel Detail,
        InstanceService Service,
        IInstanceRepository Repository,
        IInstalledGameVersionRepository Versions,
        RunningInstanceTracker Tracker,
        InstanceBackupService Backups,
        FakeProcessRunner ProcessRunner,
        FakeDotnetLocator DotnetLocator,
        IInstalledModRepository Mods,
        ModInstallService ModInstallService,
        ModUpdateChecker UpdateChecker,
        IModUpdateCheckCache UpdateCache,
        GameLogInsightsService LogInsights,
        IGameLogInsightsCache LogInsightsCache,
        InstanceDoctor Doctor,
        MockFileSystem FileSystem,
        RecordingOverlayService Overlay,
        RecordingToastService Toasts,
        string Slug);

    private static async Task<Fixture> CreateAsync(bool installVersion = true, bool linux = true)
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var clock = new FakeClock(Now);
        var service = new InstanceService(repository, fileSystem, clock);
        var record = await service.CreateAsync("Homestead", SampleVersion);

        var versions = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        if (installVersion)
        {
            versions.PrepareDirectory(SampleVersion);
            await versions.MarkCompleteAsync(SampleVersion, CancellationToken.None);
        }

        var processRunner = new FakeProcessRunner();
        var dotnetLocator = new FakeDotnetLocator();
        var tracker = new RunningInstanceTracker(service, clock);
        var backups = new InstanceBackupService(repository, fileSystem, clock);
        var launcher = new GameLauncher(
            repository, versions, dotnetLocator, tracker, new LinuxGameLaunchStrategy(fileSystem), processRunner, fileSystem, Paths, clock,
            AccountDoubles.SignedOut(), AccountDoubles.ClientSettings(fileSystem), backups);
        var environment = new FakeAppEnvironment { CurrentOperatingSystem = linux ? AppOperatingSystem.Linux : AppOperatingSystem.Windows };
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();
        var mods = ModDbDoubles.CreateRepository(fileSystem, repository, Paths);
        var modInstallService = ModDbDoubles.CreateInstallService(fileSystem, repository, mods, Paths, clock);
        var updateChecker = ModDbDoubles.CreateUpdateChecker(fileSystem, repository, mods, Paths, clock);
        var updateCache = new ModUpdateCheckCache();
        var logInsights = new GameLogInsightsService(fileSystem, mods, new ModIntegrationScanner(fileSystem), clock);
        var logInsightsCache = new GameLogInsightsCache();
        var doctor = new InstanceDoctor(repository, versions, dotnetLocator, mods, fileSystem, Paths);

        var detail = new InstanceDetailViewModel(
            record.Slug, service, repository, launcher, tracker, backups, mods, modInstallService, updateChecker, updateCache,
            logInsights, logInsightsCache, doctor,
            environment, fileSystem, overlay, toasts, new ImmediateUiDispatcher(), clock,
            new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache());

        return new Fixture(
            detail, service, repository, versions, tracker, backups, processRunner, dotnetLocator, mods, modInstallService,
            updateChecker, updateCache, logInsights, logInsightsCache, doctor, fileSystem, overlay, toasts, record.Slug);
    }

    [Fact]
    public async Task InitializeAsync_LoadsHeaderFromTheRecord()
    {
        var fixture = await CreateAsync();

        await fixture.Detail.InitializeCommand.ExecuteAsync(null);

        fixture.Detail.Name.ShouldBe("Homestead");
        fixture.Detail.VersionText.ShouldBe("1.21.3");
        fixture.Detail.ChannelBadgeTone.ShouldBe("stable");
    }

    [Fact]
    public async Task InitializeAsync_NoWorldsYet_HasWorldsFalse()
    {
        var fixture = await CreateAsync();

        await fixture.Detail.InitializeCommand.ExecuteAsync(null);

        fixture.Detail.HasWorlds.ShouldBeFalse();
        fixture.Detail.Worlds.ShouldBeEmpty();
    }

    [Fact]
    public async Task InitializeAsync_WorldFilesPresent_ListsThemMostRecentFirst()
    {
        var fixture = await CreateAsync();
        var savesDirectory = fixture.FileSystem.Path.Combine(fixture.Repository.GetDataDirectory(fixture.Slug), "Saves");
        fixture.FileSystem.AddFile(fixture.FileSystem.Path.Combine(savesDirectory, "Araya.vcdbs"), new MockFileData("a") { LastWriteTime = Now.AddDays(-1) });
        fixture.FileSystem.AddFile(fixture.FileSystem.Path.Combine(savesDirectory, "Sandbox.vcdbs"), new MockFileData("b") { LastWriteTime = Now });

        await fixture.Detail.InitializeCommand.ExecuteAsync(null);

        fixture.Detail.HasWorlds.ShouldBeTrue();
        fixture.Detail.Worlds.Select(world => world.Name).ShouldBe(["Sandbox.vcdbs", "Araya.vcdbs"]);
    }

    [Fact]
    public async Task InitializeAsync_NeverLaunched_JournalIsEmpty()
    {
        var fixture = await CreateAsync();

        await fixture.Detail.InitializeCommand.ExecuteAsync(null);

        fixture.Detail.HasEverLaunched.ShouldBeFalse();
        fixture.Detail.HasJournalContent.ShouldBeFalse();
    }

    [Fact]
    public async Task InitializeAsync_ExistingLaunchSettings_SeedTheOptionsTab()
    {
        var fixture = await CreateAsync();
        await fixture.Service.UpdateLaunchSettingsAsync(fixture.Slug, new InstanceLaunchSettings { ExtraArgs = ["--dev"] }, CancellationToken.None);

        await fixture.Detail.InitializeCommand.ExecuteAsync(null);

        fixture.Detail.OptionsTab.ExtraArgsText.ShouldBe("--dev");
    }

    [Fact]
    public async Task InitializeAsync_ExistingBackupSettings_SeedTheBackupsSection()
    {
        var fixture = await CreateAsync();
        await fixture.Service.UpdateBackupSettingsAsync(fixture.Slug, new InstanceBackupSettings { AutoBeforeLaunch = true, KeepCount = 10 }, CancellationToken.None);

        await fixture.Detail.InitializeCommand.ExecuteAsync(null);

        fixture.Detail.OptionsTab.Backups.AutoBeforeLaunch.ShouldBeTrue();
        fixture.Detail.OptionsTab.Backups.KeepCount.ShouldBe(10);
    }

    [Fact]
    public async Task SelectTab_ChangesSelectedTab()
    {
        var fixture = await CreateAsync();

        fixture.Detail.SelectTabCommand.Execute(InstanceDetailTab.Options);

        fixture.Detail.SelectedTab.ShouldBe(InstanceDetailTab.Options);
    }

    [Fact]
    public async Task PlayAsync_Success_BecomesRunningWithoutError()
    {
        var fixture = await CreateAsync();

        await fixture.Detail.PlayCommand.ExecuteAsync(null);

        fixture.Detail.IsRunning.ShouldBeTrue();
        fixture.Detail.ShowLaunchError.ShouldBeFalse();
    }

    /// <summary>
    /// Lancer TRONQUE le journal (voir <c>GameLauncher.PrepareLogFile</c>) : les pastilles que
    /// l'onglet Mods tirait du lancement précédent décrivent alors une session qui n'existe plus,
    /// et elles doivent partir au moment du clic, pas à la sortie du jeu.
    /// </summary>
    [Fact]
    public async Task PlayAsync_ClearsWhatThePreviousLaunchSaidAboutTheMods()
    {
        var fixture = await CreateAsync();
        ModDbDoubles.SeedMod(
            fixture.FileSystem,
            fixture.Mods.GetModsDirectory(fixture.Slug),
            "configlib-1.0.0.zip",
            ModDbDoubles.ModInfo("configlib", "Config lib", "1.0.0"));
        fixture.FileSystem.AddFile(
            fixture.FileSystem.Path.Combine(Paths.LogsDirectory, $"instance-{fixture.Slug}.log"),
            new MockFileData(string.Join(
                Environment.NewLine,
                "13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: configlib, game",
                "13.8.2026 21:08:23 [Client Error] [configlib] Could not resolve some dependencies:")));

        await fixture.Detail.InitializeCommand.ExecuteAsync(null);
        fixture.Detail.ModsTab.Mods.ShouldHaveSingleItem().HasLogErrors.ShouldBeTrue();

        await fixture.Detail.PlayCommand.ExecuteAsync(null);

        fixture.Detail.IsRunning.ShouldBeTrue();
        fixture.Detail.ModsTab.Mods.ShouldHaveSingleItem().HasLogErrors.ShouldBeFalse();
    }

    [Fact]
    public async Task PlayAsync_VersionNotInstalled_ShowsBannerWithInstallAction()
    {
        var fixture = await CreateAsync(installVersion: false);

        await fixture.Detail.PlayCommand.ExecuteAsync(null);

        fixture.Detail.ShowLaunchError.ShouldBeTrue();
        fixture.Detail.LaunchErrorTitle.ShouldBe("Version non installée");
        fixture.Detail.ShowInstallAction.ShouldBeTrue();
    }

    [Fact]
    public async Task PlayAsync_RuntimeMissing_ShowsBannerWithoutInstallAction()
    {
        var fixture = await CreateAsync();
        fixture.DotnetLocator.Result = RuntimeCheckResult.Missing(GameRuntimeRequirement.Known("Microsoft.NETCore.App", new Version(8, 0, 10)));

        await fixture.Detail.PlayCommand.ExecuteAsync(null);

        fixture.Detail.ShowLaunchError.ShouldBeTrue();
        fixture.Detail.LaunchErrorTitle.ShouldBe("Composant .NET manquant");
        fixture.Detail.ShowInstallAction.ShouldBeFalse();
    }

    [Fact]
    public async Task PlayAsync_AutoBackupDisabled_ShowsNoWarningToast()
    {
        var fixture = await CreateAsync();

        await fixture.Detail.PlayCommand.ExecuteAsync(null);

        fixture.Toasts.Shown.ShouldNotContain(t => t.Title == "Sauvegarde automatique ratée");
    }

    [Fact]
    public async Task PlayAsync_AutoBackupEnabledAndFails_ShowsAProminentWarningToastButStillLaunches()
    {
        var fixture = await CreateAsync();
        await fixture.Service.UpdateBackupSettingsAsync(fixture.Slug, new InstanceBackupSettings { AutoBeforeLaunch = true }, CancellationToken.None);
        // Un fichier occupe déjà l'emplacement du dossier backups/ : la sauvegarde automatique
        // échoue proprement, sans toucher au disque réel.
        fixture.FileSystem.AddFile(fixture.Backups.GetBackupsDirectory(fixture.Slug), new MockFileData("obstacle"));

        await fixture.Detail.PlayCommand.ExecuteAsync(null);

        fixture.Detail.IsRunning.ShouldBeTrue();
        fixture.Toasts.Shown.Single(t => t.Title == "Sauvegarde automatique ratée").Tone.ShouldBe(ToastTone.Warning);
    }

    [Fact]
    public async Task PlayAsync_AutoBackupEnabledAndSucceeds_ShowsNoWarningToast()
    {
        var fixture = await CreateAsync();
        await fixture.Service.UpdateBackupSettingsAsync(fixture.Slug, new InstanceBackupSettings { AutoBeforeLaunch = true }, CancellationToken.None);

        await fixture.Detail.PlayCommand.ExecuteAsync(null);

        fixture.Toasts.Shown.ShouldNotContain(t => t.Title == "Sauvegarde automatique ratée");
        (await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task PlayAsync_MacStrategy_ShowsBanner()
    {
        var fixture = await CreateAsync();
        var macDetail = new InstanceDetailViewModel(
            fixture.Slug,
            fixture.Service,
            fixture.Repository,
            new GameLauncher(
                fixture.Repository, fixture.Versions, fixture.DotnetLocator, fixture.Tracker, new MacGameLaunchStrategy(), fixture.ProcessRunner,
                fixture.FileSystem, Paths, new FakeClock(Now), AccountDoubles.SignedOut(), AccountDoubles.ClientSettings(fixture.FileSystem), fixture.Backups),
            fixture.Tracker,
            fixture.Backups,
            fixture.Mods,
            fixture.ModInstallService,
            fixture.UpdateChecker,
            fixture.UpdateCache,
            fixture.LogInsights,
            fixture.LogInsightsCache,
            fixture.Doctor,
            new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.MacOs },
            fixture.FileSystem,
            fixture.Overlay,
            fixture.Toasts,
            new ImmediateUiDispatcher(),
            new FakeClock(Now),
            new FakeExternalUrlOpener(),
            ModDbDoubles.CreateLogoCache());

        await macDetail.PlayCommand.ExecuteAsync(null);

        macDetail.LaunchErrorTitle.ShouldBe("macOS non pris en charge");
    }

    [Fact]
    public async Task GoToVersionsCommand_RaisesNavigateToVersionsRequested()
    {
        var fixture = await CreateAsync();
        var raised = false;
        fixture.Detail.NavigateToVersionsRequested += (_, _) => raised = true;

        fixture.Detail.GoToVersionsCommand.Execute(null);

        raised.ShouldBeTrue();
    }

    [Fact]
    public async Task RequestStop_ConfirmedFromDialog_KillsTheProcessAndReloadsHeader()
    {
        var fixture = await CreateAsync();
        var process = new FakeRunningProcess();
        fixture.ProcessRunner.NextProcessFactory = _ => process;
        await fixture.Detail.PlayCommand.ExecuteAsync(null);

        fixture.Detail.RequestStopCommand.Execute(null);
        var dialog = fixture.Overlay.Active.ShouldBeOfType<StopInstanceDialogViewModel>();
        dialog.ConfirmCommand.Execute(null);

        process.IsKilled.ShouldBeTrue();
    }

    [Fact]
    public async Task Back_RaisesBackRequested()
    {
        var fixture = await CreateAsync();
        var raised = false;
        fixture.Detail.BackRequested += (_, _) => raised = true;

        fixture.Detail.BackCommand.Execute(null);

        raised.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_ConfirmedFromDialog_RaisesBackRequested()
    {
        var fixture = await CreateAsync();
        var raised = false;
        fixture.Detail.BackRequested += (_, _) => raised = true;

        fixture.Detail.DeleteCommand.Execute(null);
        var dialog = fixture.Overlay.Active.ShouldBeOfType<DeleteInstanceDialogViewModel>();
        await dialog.ConfirmCommand.ExecuteAsync(null);

        raised.ShouldBeTrue();
    }

    [Fact]
    public async Task Duplicate_ConfirmedFromDialog_ShowsSuccessToast()
    {
        var fixture = await CreateAsync();

        fixture.Detail.DuplicateCommand.Execute(null);
        var dialog = fixture.Overlay.Active.ShouldBeOfType<DuplicateDialogViewModel>();
        await dialog.ConfirmCommand.ExecuteAsync(null);

        fixture.Toasts.Shown.ShouldHaveSingleItem().Title.ShouldBe("Instance dupliquée");
    }

    [Fact]
    public async Task Rename_ConfirmedFromDialog_ReloadsTheDisplayedName()
    {
        var fixture = await CreateAsync();
        await fixture.Detail.InitializeCommand.ExecuteAsync(null);

        fixture.Detail.RenameCommand.Execute(null);
        var dialog = fixture.Overlay.Active.ShouldBeOfType<RenameDialogViewModel>();
        dialog.Name = "Nouveau nom";
        await dialog.ConfirmCommand.ExecuteAsync(null);

        fixture.Detail.Name.ShouldBe("Nouveau nom");
    }

    [Fact]
    public async Task RefreshJournal_AfterALaunch_ReadsTheLogFile()
    {
        var fixture = await CreateAsync();
        await fixture.Detail.PlayCommand.ExecuteAsync(null);

        fixture.Detail.RefreshJournalCommand.Execute(null);

        fixture.Detail.HasEverLaunched.ShouldBeTrue();
        fixture.Detail.JournalContent.ShouldContain("Homestead");
    }

    [Fact]
    public async Task Dispose_TrackerStatusChangesNoLongerUpdateIsRunning()
    {
        var fixture = await CreateAsync();
        fixture.Detail.Dispose();

        await fixture.Detail.PlayCommand.ExecuteAsync(null);

        fixture.Detail.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fixture = await CreateAsync();
        var environment = new FakeAppEnvironment();
        var dispatcher = new ImmediateUiDispatcher();
        var clock = new FakeClock(Now);

        Should.Throw<ArgumentException>(() => new InstanceDetailViewModel(
            string.Empty, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, null!, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, null!, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, null!, fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), null!, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, null!, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, null!, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, null!, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, null!, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, null!, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, null!, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, null!, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, null!, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, null!, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, null!, dispatcher, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, null!, clock, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, null!, new FakeExternalUrlOpener(), ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, null!, ModDbDoubles.CreateLogoCache()));
        Should.Throw<ArgumentNullException>(() => new InstanceDetailViewModel(
            fixture.Slug, fixture.Service, fixture.Repository, MakeLauncher(fixture), fixture.Tracker, fixture.Backups, fixture.Mods, fixture.ModInstallService, fixture.UpdateChecker, fixture.UpdateCache, fixture.LogInsights, fixture.LogInsightsCache, fixture.Doctor, environment, fixture.FileSystem, fixture.Overlay, fixture.Toasts, dispatcher, clock, new FakeExternalUrlOpener(), null!));
    }

    private static GameLauncher MakeLauncher(Fixture fixture) => new(
        fixture.Repository, fixture.Versions, fixture.DotnetLocator, fixture.Tracker,
        new LinuxGameLaunchStrategy(fixture.FileSystem), fixture.ProcessRunner, fixture.FileSystem, Paths, new FakeClock(Now),
        AccountDoubles.SignedOut(), AccountDoubles.ClientSettings(fixture.FileSystem), fixture.Backups);
}