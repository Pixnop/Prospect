using System.Reflection;

using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Settings;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Settings;

/// <summary>
/// <see cref="SettingsAboutViewModel"/> : version lue de l'assembly (jamais codée en dur, voir
/// « à-propos version »), licence affichée, lien GitHub ouvert via l'ouvreur externe.
/// </summary>
public sealed class SettingsAboutViewModelTests
{
    [Fact]
    public void Constructor_NullUrlOpener_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new SettingsAboutViewModel(null!));
    }

    [Fact]
    public void AppVersion_MatchesTheAssemblyInformationalVersion()
    {
        var viewModel = new SettingsAboutViewModel(new FakeExternalUrlOpener());

        var expected = typeof(SettingsAboutViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        viewModel.AppVersion.ShouldBe(expected);
        viewModel.AppVersion.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void License_IsGpl30()
    {
        var viewModel = new SettingsAboutViewModel(new FakeExternalUrlOpener());

        viewModel.License.ShouldBe("GPL-3.0");
    }

    [Fact]
    public async Task OpenGitHubAsync_OpensTheRepositoryUrl()
    {
        var urlOpener = new FakeExternalUrlOpener();
        var viewModel = new SettingsAboutViewModel(urlOpener);

        await viewModel.OpenGitHubCommand.ExecuteAsync(null);

        urlOpener.Opened.ShouldHaveSingleItem();
        urlOpener.Opened[0].ShouldBe(new Uri("https://github.com/Pixnop/Prospect"));
    }

    [Fact]
    public async Task OpenWebsiteAsync_OpensTheProspectWebUrl()
    {
        var urlOpener = new FakeExternalUrlOpener();
        var viewModel = new SettingsAboutViewModel(urlOpener);

        await viewModel.OpenWebsiteCommand.ExecuteAsync(null);

        urlOpener.Opened.ShouldHaveSingleItem();
        urlOpener.Opened[0].ShouldBe(new Uri("https://leonfvt.fr/prospect-web/"));
    }
}