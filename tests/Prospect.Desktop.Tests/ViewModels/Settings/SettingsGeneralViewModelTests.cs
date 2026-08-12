using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Settings;
using Prospect.Core.Settings.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.ViewModels.Settings;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Settings;

/// <summary>
/// <see cref="SettingsGeneralViewModel"/> : reflète le thème persisté, sauvegarde à chaque
/// changement, et verrouille la langue sur le français (« langue verrouillée », voir la docstring
/// de la classe).
/// </summary>
public sealed class SettingsGeneralViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");

    private static SettingsService CreateSettingsService(MockFileSystem fileSystem)
        => new(fileSystem, Paths, new JsonFileStore(fileSystem), new SettingsMigrationPipeline([]));

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new SettingsGeneralViewModel(null!));
    }

    [Fact]
    public void Constructor_ReflectsThePersistedTheme()
    {
        var settings = CreateSettingsService(new MockFileSystem());

        var viewModel = new SettingsGeneralViewModel(settings);

        viewModel.ThemeChoice.ShouldBe(ThemePreference.Dark);
    }

    [Fact]
    public async Task SelectTheme_UpdatesThemeChoiceAndPersists()
    {
        var fileSystem = new MockFileSystem();
        var settings = CreateSettingsService(fileSystem);
        var viewModel = new SettingsGeneralViewModel(settings);
        // La sauvegarde est un fire-and-forget déclenché par le changement de propriété (une
        // vue ne peut pas attendre un binding) : on la guette par l'évènement plutôt que par un
        // délai arbitraire, pour un test déterministe.
        var saved = new TaskCompletionSource();
        settings.Changed += (_, _) => saved.TrySetResult();

        viewModel.SelectThemeCommand.Execute(ThemePreference.Light);

        // La valeur observable côté ViewModel change immédiatement, seule la persistance est différée.
        viewModel.ThemeChoice.ShouldBe(ThemePreference.Light);
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        settings.Current.Theme.ShouldBe(ThemePreference.Light);
    }

    [Fact]
    public void SelectTheme_System_IsSelectable()
    {
        var settings = CreateSettingsService(new MockFileSystem());
        var viewModel = new SettingsGeneralViewModel(settings);

        viewModel.SelectThemeCommand.Execute(ThemePreference.System);

        viewModel.ThemeChoice.ShouldBe(ThemePreference.System);
    }

    [Fact]
    public void Language_IsLockedToFrench()
    {
        var settings = CreateSettingsService(new MockFileSystem());
        var viewModel = new SettingsGeneralViewModel(settings);

        viewModel.SelectedLanguageLabel.ShouldBe("Français");
        viewModel.IsLanguageEditable.ShouldBeFalse();
    }
}