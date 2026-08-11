using Prospect.Core.Common;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Common;

public sealed class ExternalUrlOpenerTests
{
    private static readonly Uri ModPage = new("https://mods.vintagestory.at/show/mod/1783");

    private static (ExternalUrlOpener Opener, FakeProcessRunner Runner) Create(AppOperatingSystem operatingSystem)
    {
        var runner = new FakeProcessRunner();

        return (new ExternalUrlOpener(runner, new FakeAppEnvironment { CurrentOperatingSystem = operatingSystem }), runner);
    }

    [Theory]
    [InlineData(AppOperatingSystem.Linux, "xdg-open")]
    [InlineData(AppOperatingSystem.MacOs, "open")]
    [InlineData(AppOperatingSystem.Windows, "explorer")]
    public async Task OpenAsync_UsesTheOpenCommandOfTheCurrentSystem(AppOperatingSystem operatingSystem, string expected)
    {
        var (opener, runner) = Create(operatingSystem);

        (await opener.OpenAsync(ModPage, CancellationToken.None)).ShouldBeTrue();

        var request = runner.Requests.ShouldHaveSingleItem();
        request.FileName.ShouldBe(expected);
        request.Arguments.ShouldBe([ModPage.AbsoluteUri]);
    }

    [Fact]
    public async Task OpenAsync_NonZeroExitCode_IsReportedAsAFailureOnLinux()
    {
        var (opener, runner) = Create(AppOperatingSystem.Linux);
        runner.ExitCode = 3;

        (await opener.OpenAsync(ModPage, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task OpenAsync_NonZeroExitCodeOnWindows_IsStillASuccess()
    {
        // explorer.exe rend un code non nul même quand il a bien ouvert le navigateur.
        var (opener, runner) = Create(AppOperatingSystem.Windows);
        runner.ExitCode = 1;

        (await opener.OpenAsync(ModPage, CancellationToken.None)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.invalid/x")]
    public async Task OpenAsync_NonHttpScheme_IsRefusedWithoutRunningAnything(string url)
    {
        var (opener, runner) = Create(AppOperatingSystem.Linux);

        (await opener.OpenAsync(new Uri(url), CancellationToken.None)).ShouldBeFalse();
        runner.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task OpenAsync_CommandMissingFromTheSystem_IsADisappointmentNotACrash()
    {
        var (opener, runner) = Create(AppOperatingSystem.Linux);
        runner.Failure = new InvalidOperationException("xdg-open introuvable");

        (await opener.OpenAsync(ModPage, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task OpenAsync_NullUrl_IsRejected()
    {
        var (opener, _) = Create(AppOperatingSystem.Linux);

        await Should.ThrowAsync<ArgumentNullException>(() => opener.OpenAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullDependencies_AreRejected()
    {
        Should.Throw<ArgumentNullException>(() => new ExternalUrlOpener(null!, new FakeAppEnvironment()));
        Should.Throw<ArgumentNullException>(() => new ExternalUrlOpener(new FakeProcessRunner(), null!));
    }
}