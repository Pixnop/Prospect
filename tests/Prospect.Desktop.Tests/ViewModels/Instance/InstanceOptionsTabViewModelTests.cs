using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Instance;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Instance;

public sealed class InstanceOptionsTabViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    private static (InstanceOptionsTabViewModel ViewModel, IInstanceRepository Repository, string Slug) Create(
        InstanceLaunchSettings? settings = null, bool linux = true)
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var service = new InstanceService(repository, fileSystem, new FakeClock(Now));
        var created = service.CreateAsync("Homestead", GameVersion.Parse("1.21.3")).GetAwaiter().GetResult();
        var environment = new FakeAppEnvironment
        {
            CurrentOperatingSystem = linux ? AppOperatingSystem.Linux : AppOperatingSystem.Windows,
        };
        var viewModel = new InstanceOptionsTabViewModel(created.Slug, settings ?? InstanceLaunchSettings.Empty, service, environment, new RecordingToastService());

        return (viewModel, repository, created.Slug);
    }

    [Fact]
    public void Constructor_ExistingExtraArgs_OneArgumentPerLine()
    {
        var settings = new InstanceLaunchSettings { ExtraArgs = ["--logfile", "custom.log"] };

        var (viewModel, _, _) = Create(settings);

        viewModel.ExtraArgsText.ShouldBe($"--logfile{Environment.NewLine}custom.log");
    }

    [Fact]
    public void Constructor_ExistingEnvVars_ExcludesMesaGlThreadFromFreeTextAndTogglesCheckbox()
    {
        var settings = new InstanceLaunchSettings
        {
            Env = new Dictionary<string, string> { ["MESA_GLTHREAD"] = "true", ["FOO"] = "bar" },
        };

        var (viewModel, _, _) = Create(settings);

        viewModel.EnvVarsText.ShouldBe("FOO=bar");
        viewModel.MesaGlThreadEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_OnLinux_ShowsMesaGlThreadToggle()
    {
        var (viewModel, _, _) = Create(linux: true);

        viewModel.ShowMesaGlThreadToggle.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_OnWindows_HidesMesaGlThreadToggle()
    {
        var (viewModel, _, _) = Create(linux: false);

        viewModel.ShowMesaGlThreadToggle.ShouldBeFalse();
    }

    [Fact]
    public void EnvVarsError_ValidKeyValueLines_IsNull()
    {
        var (viewModel, _, _) = Create();

        viewModel.EnvVarsText = $"FOO=bar{Environment.NewLine}BAZ=qux";

        viewModel.EnvVarsError.ShouldBeNull();
        viewModel.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void EnvVarsError_LineWithoutEqualsSign_IsReported()
    {
        var (viewModel, _, _) = Create();

        viewModel.EnvVarsText = "not-a-valid-line";

        viewModel.EnvVarsError.ShouldNotBeNull();
        viewModel.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void EnvVarsError_LineWithEmptyKey_IsReported()
    {
        var (viewModel, _, _) = Create();

        viewModel.EnvVarsText = "=value-without-a-key";

        viewModel.EnvVarsError.ShouldNotBeNull();
    }

    [Fact]
    public void EnvVarsError_BlankLinesAreIgnored()
    {
        var (viewModel, _, _) = Create();

        viewModel.EnvVarsText = $"FOO=bar{Environment.NewLine}{Environment.NewLine}   {Environment.NewLine}BAZ=qux";

        viewModel.EnvVarsError.ShouldBeNull();
    }

    [Fact]
    public async Task SaveAsync_ValidInput_RoundTripsThroughTheRepository()
    {
        var (viewModel, repository, slug) = Create();
        viewModel.ExtraArgsText = $"--dev{Environment.NewLine}  {Environment.NewLine}--logfile";
        viewModel.EnvVarsText = "FOO=bar";

        await viewModel.SaveCommand.ExecuteAsync(null);

        var reloaded = await repository.LoadAsync(slug, CancellationToken.None);
        reloaded.Metadata.Launch.ExtraArgs.ShouldBe(["--dev", "--logfile"]);
        reloaded.Metadata.Launch.Env["FOO"].ShouldBe("bar");
    }

    [Fact]
    public async Task SaveAsync_MesaGlThreadEnabled_AddsTheVariable()
    {
        var (viewModel, repository, slug) = Create();
        viewModel.MesaGlThreadEnabled = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var reloaded = await repository.LoadAsync(slug, CancellationToken.None);
        reloaded.Metadata.Launch.Env["MESA_GLTHREAD"].ShouldBe("true");
    }

    [Fact]
    public async Task SaveAsync_MesaGlThreadDisabledAfterBeingEnabled_RemovesTheVariable()
    {
        var settings = new InstanceLaunchSettings { Env = new Dictionary<string, string> { ["MESA_GLTHREAD"] = "true" } };
        var (viewModel, repository, slug) = Create(settings);
        viewModel.MesaGlThreadEnabled.ShouldBeTrue();

        viewModel.MesaGlThreadEnabled = false;
        await viewModel.SaveCommand.ExecuteAsync(null);

        var reloaded = await repository.LoadAsync(slug, CancellationToken.None);
        reloaded.Metadata.Launch.Env.ShouldNotContainKey("MESA_GLTHREAD");
    }

    [Fact]
    public void SaveCommand_InvalidEnvVars_CannotExecute()
    {
        var (viewModel, _, _) = Create();

        viewModel.EnvVarsText = "not-valid";

        viewModel.SaveCommand.CanExecute(null).ShouldBeFalse();
    }
}