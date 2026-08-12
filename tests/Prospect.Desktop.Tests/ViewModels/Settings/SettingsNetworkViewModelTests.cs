using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Settings;
using Prospect.Core.Settings.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.ViewModels.Settings;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Settings;

/// <summary>
/// <see cref="SettingsNetworkViewModel"/> : reflète le parallélisme persisté, sauvegarde à chaque
/// changement, et ne propose que des choix déjà bornés (« bornes de téléchargements »).
/// </summary>
public sealed class SettingsNetworkViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");

    private static SettingsService CreateSettingsService(MockFileSystem fileSystem)
        => new(fileSystem, Paths, new JsonFileStore(fileSystem), new SettingsMigrationPipeline([]));

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new SettingsNetworkViewModel(null!));
    }

    [Fact]
    public void Constructor_ReflectsThePersistedConcurrency()
    {
        var settings = CreateSettingsService(new MockFileSystem());

        var viewModel = new SettingsNetworkViewModel(settings);

        viewModel.MaxParallelDownloads.ShouldBe(DownloadPreferences.Default.MaxParallelDownloads);
    }

    [Fact]
    public void AvailableConcurrencyChoices_AreAllWithinTheModelBounds()
    {
        var settings = CreateSettingsService(new MockFileSystem());
        var viewModel = new SettingsNetworkViewModel(settings);

        foreach (var choice in viewModel.AvailableConcurrencyChoices)
        {
            choice.ShouldBeInRange(DownloadPreferences.MinParallelDownloads, DownloadPreferences.MaxParallelDownloadsCeiling);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task MaxParallelDownloads_Changed_PersistsTheNewValue(int choice)
    {
        var fileSystem = new MockFileSystem();
        var settings = CreateSettingsService(fileSystem);
        var viewModel = new SettingsNetworkViewModel(settings);
        var saved = new TaskCompletionSource();
        settings.Changed += (_, _) => saved.TrySetResult();

        viewModel.MaxParallelDownloads = choice;

        await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        settings.Current.Downloads.MaxParallelDownloads.ShouldBe(choice);
    }
}