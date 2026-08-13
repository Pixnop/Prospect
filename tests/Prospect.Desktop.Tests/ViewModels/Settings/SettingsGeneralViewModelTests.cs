using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Settings;
using Prospect.Core.Settings.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Settings;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Settings;

/// <summary>
/// <see cref="SettingsGeneralViewModel"/> : reflète le thème et la langue persistés, sauvegarde à
/// chaque changement, et n'applique JAMAIS la langue à chaud (elle prend effet au redémarrage, voir
/// la docstring de la classe et LanguageService).
/// </summary>
public sealed class SettingsGeneralViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");

    private static SettingsService CreateSettingsService(MockFileSystem fileSystem)
        => new(fileSystem, Paths, new JsonFileStore(fileSystem), new SettingsMigrationPipeline([]), new FakeUiCulture());

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
    public void Language_IsSelectableAndDefaultsToTheFirstEntry()
    {
        var settings = CreateSettingsService(new MockFileSystem());
        var viewModel = new SettingsGeneralViewModel(settings);

        viewModel.IsLanguageEditable.ShouldBeTrue();
        viewModel.SelectedLanguageIndex.ShouldBe(0);
        viewModel.SelectedLanguage.ShouldBe(ProspectSettings.French);
    }

    [Fact]
    public async Task Constructor_ReflectsThePersistedLanguage()
    {
        var fileSystem = new MockFileSystem();
        var settings = CreateSettingsService(fileSystem);
        await settings.UpdateAsync(current => current with { Language = ProspectSettings.English });

        var viewModel = new SettingsGeneralViewModel(settings);

        viewModel.SelectedLanguageIndex.ShouldBe(1);
        viewModel.SelectedLanguage.ShouldBe(ProspectSettings.English);
    }

    [Fact]
    public async Task SelectLanguage_PersistsTheChoice()
    {
        var fileSystem = new MockFileSystem();
        var settings = CreateSettingsService(fileSystem);
        var viewModel = new SettingsGeneralViewModel(settings);
        // Même dispositif que pour le thème : la sauvegarde est un fire-and-forget déclenché par le
        // changement de propriété, guetté par l'évènement plutôt que par un délai arbitraire.
        var saved = new TaskCompletionSource();
        settings.Changed += (_, _) => saved.TrySetResult();

        viewModel.SelectedLanguageIndex = 1;

        viewModel.SelectedLanguage.ShouldBe(ProspectSettings.English);
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        settings.Current.Language.ShouldBe(ProspectSettings.English);
    }

    [Fact]
    public async Task SelectLanguage_ChangesNothingInTheRunningApplication()
    {
        // La décision d'architecture, rendue vérifiable : choisir l'anglais persiste le réglage et
        // NE retraduit rien (la vue affiche « prend effet au redémarrage »). UiText reste donc sur
        // sa table courante, celle que le harnais épingle en français.
        var fileSystem = new MockFileSystem();
        var settings = CreateSettingsService(fileSystem);
        var viewModel = new SettingsGeneralViewModel(settings);
        var saved = new TaskCompletionSource();
        settings.Changed += (_, _) => saved.TrySetResult();

        viewModel.SelectedLanguageIndex = 1;
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        UiText.Language.ShouldBe(ProspectSettings.French);
        UiText.Shell.NavSettings.ShouldBe("Réglages");
    }

    [Fact]
    public async Task SelectLanguage_BackToFrench_PersistsTheChoice()
    {
        var fileSystem = new MockFileSystem();
        var settings = CreateSettingsService(fileSystem);
        await settings.UpdateAsync(current => current with { Language = ProspectSettings.English });
        var viewModel = new SettingsGeneralViewModel(settings);
        var saved = new TaskCompletionSource();
        settings.Changed += (_, _) => saved.TrySetResult();

        viewModel.SelectedLanguageIndex = 0;
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        settings.Current.Language.ShouldBe(ProspectSettings.French);
    }
}