using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Launching;
using Prospect.Core.Runtime;
using Prospect.Desktop.ViewModels.Instance;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Instance;

public sealed class LaunchErrorPresenterTests
{
    [Fact]
    public void Describe_GameVersionNotInstalled_ProposesInstallAction()
    {
        var exception = GameVersionNotInstalledException.For(GameVersion.Parse("1.21.3"));

        var presentation = LaunchErrorPresenter.Describe(exception);

        presentation.Title.ShouldBe("Version non installée");
        presentation.Message.ShouldBe(exception.Message);
        presentation.Action.ShouldBe(LaunchErrorAction.InstallVersion);
    }

    [Fact]
    public void Describe_RuntimeNotAvailable_NoAction()
    {
        var exception = RuntimeNotAvailableException.For(GameRuntimeRequirement.Known("Microsoft.NETCore.App", new Version(8, 0, 10)));

        var presentation = LaunchErrorPresenter.Describe(exception);

        presentation.Title.ShouldBe("Composant .NET manquant");
        presentation.Message.ShouldContain("Microsoft.NETCore.App");
        presentation.Action.ShouldBe(LaunchErrorAction.None);
    }

    [Fact]
    public void Describe_MacLaunchNotSupported_NoAction()
    {
        var presentation = LaunchErrorPresenter.Describe(new MacLaunchNotSupportedException());

        presentation.Title.ShouldBe("macOS non pris en charge");
        presentation.Action.ShouldBe(LaunchErrorAction.None);
    }

    [Fact]
    public void Describe_InstanceAlreadyRunning_NoAction()
    {
        var presentation = LaunchErrorPresenter.Describe(new InstanceAlreadyRunningException("homestead"));

        presentation.Title.ShouldBe("Session déjà en cours");
        presentation.Action.ShouldBe(LaunchErrorAction.None);
    }

    [Fact]
    public void Describe_UnrecognizedException_FallsBackToGenericTitle()
    {
        var presentation = LaunchErrorPresenter.Describe(new InstanceNotFoundException("ghost"));

        presentation.Title.ShouldBe("Lancement impossible");
        presentation.Message.ShouldBe("Aucune instance trouvée pour le slug 'ghost'.");
        presentation.Action.ShouldBe(LaunchErrorAction.None);
    }

    [Fact]
    public void Describe_Null_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => LaunchErrorPresenter.Describe(null!));
}