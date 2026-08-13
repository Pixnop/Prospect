using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Auth;
using Prospect.Core.Backups;
using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Launching;
using Prospect.Core.Runtime;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Auth;
using Prospect.Core.Tests.Http;
using Prospect.Core.Tests.Instances;
using Prospect.Core.Tests.Launching;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Common;

/// <summary>
/// L'audit demandé après le rapport de terrain Windows, appelant par appelant d'<see cref="IProcessRunner"/> :
/// lancement du jeu, détection du runtime, ouverture d'une URL ou d'un dossier, installeur Windows.
/// </summary>
/// <remarks>
/// Deux niveaux d'assertion à chaque fois. D'abord la LISTE : un argument reste un jeton, ce qui est
/// le contrat de <c>ProcessStartInfo.ArgumentList</c> et ce qui vaut tel quel sous Linux et macOS,
/// où rien n'est recollé. Ensuite la LIGNE que Windows fabriquerait à partir de cette liste, rendue
/// par <see cref="ProcessCommandLine"/> et redécoupée par le parseur du CRT : c'est là que les
/// espaces, accents et esperluettes d'un chemin de données pourraient se perdre.
/// </remarks>
public sealed class ProcessArgumentTransmissionTests
{
    /// <summary>Racine à espaces, apostrophe et accent : le profil Windows d'un vrai utilisateur.</summary>
    private static readonly AppPaths AwkwardPaths = new(new FakeAppEnvironment(), @"C:\Users\Jean Dupont\AppData\Roaming\Prospect");

    private static readonly DateTimeOffset Now = new(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GameLaunch_DataPathUnderAnAwkwardRoot_StaysOneArgumentThroughTheWindowsCommandLine()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var instanceRepository = new FileSystemInstanceRepository(fileSystem, AwkwardPaths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var instanceService = new InstanceService(instanceRepository, fileSystem, clock);
        var versionRepository = new FileSystemInstalledGameVersionRepository(fileSystem, AwkwardPaths);
        var processRunner = new FakeProcessRunner();
        var launcher = new GameLauncher(
            instanceRepository,
            versionRepository,
            new FakeDotnetLocator(),
            new RunningInstanceTracker(instanceService, clock),
            new WindowsGameLaunchStrategy(fileSystem),
            processRunner,
            fileSystem,
            AwkwardPaths,
            clock,
            new VsAccountService(
                new VsAccountClient(new HttpClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Text("{}")), disposeHandler: false)),
                new FakeSecretStore()),
            new ClientSettingsSessionWriter(fileSystem, new JsonFileStore(fileSystem)),
            new InstanceBackupService(instanceRepository, fileSystem, clock));

        var version = GameVersion.Parse("1.22.6");
        var record = await instanceService.CreateAsync("Homestead", version, CancellationToken.None);
        versionRepository.PrepareDirectory(version);
        await versionRepository.MarkCompleteAsync(version, CancellationToken.None);

        // Des arguments supplémentaires qui portent eux aussi des espaces, comme un joueur en écrit.
        await instanceService.UpdateLaunchSettingsAsync(
            record.Slug,
            new InstanceLaunchSettings { ExtraArgs = ["--openWorld=Mon monde à moi", "--tracelog"] },
            CancellationToken.None);

        await launcher.LaunchAsync(record.Slug, cancellationToken: CancellationToken.None);

        var request = processRunner.StartRequests.ShouldHaveSingleItem();
        var dataPath = instanceRepository.GetDataDirectory(record.Slug);
        request.Arguments.ShouldBe([$"--dataPath={dataPath}", "--openWorld=Mon monde à moi", "--tracelog"]);

        WindowsArgvParser.Parse(ProcessCommandLine.Render(request)).ShouldBe(request.Arguments);
    }

    [Fact]
    public async Task DotnetLocator_AsksForTheRuntimeListAsASingleFlag()
    {
        var processRunner = new FakeProcessRunner { StandardOutput = string.Empty };

        await new DotnetLocator(processRunner, new MockFileSystem(), new FakeClock(Now))
            .GetInstalledRuntimesAsync(CancellationToken.None);

        var request = processRunner.Requests.ShouldHaveSingleItem();
        request.FileName.ShouldBe("dotnet");
        request.Arguments.ShouldBe(["--list-runtimes"]);
        WindowsArgvParser.Parse(ProcessCommandLine.Render(request)).ShouldBe(["--list-runtimes"]);
    }

    [Theory]
    [InlineData(AppOperatingSystem.Windows, "explorer")]
    [InlineData(AppOperatingSystem.MacOs, "open")]
    [InlineData(AppOperatingSystem.Linux, "xdg-open")]
    public async Task ExternalUrlOpener_FolderWithSpaces_IsHandedOverAsOneArgument(AppOperatingSystem operatingSystem, string expectedCommand)
    {
        var processRunner = new FakeProcessRunner();
        var opener = new ExternalUrlOpener(processRunner, new FakeAppEnvironment { CurrentOperatingSystem = operatingSystem });

        await opener.OpenFolderAsync(AwkwardPaths.RootDirectory, CancellationToken.None);

        var request = processRunner.Requests.ShouldHaveSingleItem();
        request.FileName.ShouldBe(expectedCommand);
        request.Arguments.ShouldBe([AwkwardPaths.RootDirectory]);
        WindowsArgvParser.Parse(ProcessCommandLine.Render(request)).ShouldBe([AwkwardPaths.RootDirectory]);
    }

    [Fact]
    public async Task ExternalUrlOpener_UrlWithAQueryString_KeepsItWhole()
    {
        var processRunner = new FakeProcessRunner();
        var opener = new ExternalUrlOpener(processRunner, new FakeAppEnvironment { CurrentOperatingSystem = AppOperatingSystem.Linux });
        var url = new Uri("https://mods.vintagestory.at/show/mod/1783?tab=files&sort=desc");

        await opener.OpenAsync(url, CancellationToken.None);

        var request = processRunner.Requests.ShouldHaveSingleItem();
        WindowsArgvParser.Parse(ProcessCommandLine.Render(request)).ShouldBe([url.AbsoluteUri]);
    }

    [Fact]
    public async Task WindowsInstaller_EveryFlagAndTheTargetSurviveTheWindowsCommandLine()
    {
        var target = new MockFileSystem().Path.Combine(AwkwardPaths.VersionsDirectory, "1.22.6");
        var processRunner = new FakeProcessRunner();

        await new WindowsGameInstallStrategy(new MockFileSystem(), processRunner, NullAppLog.Instance)
            .InstallAsync(@"C:\cache\vs_install_win-x64_1.22.6.exe", target, cancellationToken: CancellationToken.None);

        var request = processRunner.Requests.ShouldHaveSingleItem();
        WindowsArgvParser.Parse(ProcessCommandLine.Render(request)).ShouldBe(request.Arguments);
        InnoSetupParamParser.Parse(ProcessCommandLine.Render(request)).ShouldBe(request.Arguments);
    }
}