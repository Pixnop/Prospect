using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Settings;
using Prospect.Core.Settings.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Settings;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Settings;

/// <summary>
/// <see cref="SettingsGeneralViewModel"/> : reflète le thème, le fond et la langue persistés,
/// sauvegarde à chaque changement, et n'applique JAMAIS la langue à chaud (elle prend effet au
/// redémarrage, voir la docstring de la classe et LanguageService).
/// </summary>
public sealed class SettingsGeneralViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");

    private static SettingsService CreateSettingsService(MockFileSystem fileSystem)
        => new(fileSystem, Paths, new JsonFileStore(fileSystem), new SettingsMigrationPipeline([]), new FakeUiCulture());

    // Le service de fond se construit sans application graphique (il ne décode rien avant qu'une
    // vignette ne soit lue, voir sa docstring) : ces tests restent donc des [Fact] ordinaires.
    private static SettingsGeneralViewModel CreateViewModel(SettingsService settings)
        => new(settings, new BackdropService(settings));

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        var settings = CreateSettingsService(new MockFileSystem());

        Should.Throw<ArgumentNullException>(() => new SettingsGeneralViewModel(null!, new BackdropService(settings)));
        Should.Throw<ArgumentNullException>(() => new SettingsGeneralViewModel(settings, null!));
    }

    [Fact]
    public void Constructor_ReflectsThePersistedTheme()
    {
        var settings = CreateSettingsService(new MockFileSystem());

        var viewModel = CreateViewModel(settings);

        viewModel.ThemeChoice.ShouldBe(ThemePreference.Dark);
    }

    [Fact]
    public async Task SelectTheme_UpdatesThemeChoiceAndPersists()
    {
        var fileSystem = new MockFileSystem();
        var settings = CreateSettingsService(fileSystem);
        var viewModel = CreateViewModel(settings);
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
        var viewModel = CreateViewModel(settings);

        viewModel.SelectThemeCommand.Execute(ThemePreference.System);

        viewModel.ThemeChoice.ShouldBe(ThemePreference.System);
    }

    [Fact]
    public void Language_IsSelectableAndDefaultsToTheFirstEntry()
    {
        var settings = CreateSettingsService(new MockFileSystem());
        var viewModel = CreateViewModel(settings);

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

        var viewModel = CreateViewModel(settings);

        viewModel.SelectedLanguageIndex.ShouldBe(1);
        viewModel.SelectedLanguage.ShouldBe(ProspectSettings.English);
    }

    [Fact]
    public async Task SelectLanguage_PersistsTheChoice()
    {
        var fileSystem = new MockFileSystem();
        var settings = CreateSettingsService(fileSystem);
        var viewModel = CreateViewModel(settings);
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
        var viewModel = CreateViewModel(settings);
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
        var viewModel = CreateViewModel(settings);
        var saved = new TaskCompletionSource();
        settings.Changed += (_, _) => saved.TrySetResult();

        viewModel.SelectedLanguageIndex = 0;
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        settings.Current.Language.ShouldBe(ProspectSettings.French);
    }

    // ── Fond de fenêtre ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_OffersEveryCatalogueBackdropWithTheDefaultSelected()
    {
        var settings = CreateSettingsService(new MockFileSystem());

        var viewModel = CreateViewModel(settings);

        viewModel.BackdropChoices.Select(choice => choice.Key).ShouldBe(BackdropCatalog.Keys);
        viewModel.SelectedBackdropKey.ShouldBe(BackdropCatalog.Default);
        viewModel.BackdropChoices.Single(choice => choice.IsSelected).Key.ShouldBe(BackdropCatalog.Default);
        // Un nom traduit sous chaque vignette, jamais la clé brute.
        viewModel.BackdropChoices.ShouldAllBe(choice => choice.Label.Length > 0 && choice.Label != choice.Key);
    }

    [Fact]
    public async Task Constructor_ReflectsThePersistedBackdrop()
    {
        var fileSystem = new MockFileSystem();
        var settings = CreateSettingsService(fileSystem);
        await settings.UpdateAsync(current => current with { Backdrop = "village-lane" });

        var viewModel = CreateViewModel(settings);

        viewModel.SelectedBackdropKey.ShouldBe("village-lane");
        viewModel.BackdropChoices.Single(choice => choice.IsSelected).Key.ShouldBe("village-lane");
    }

    [Fact]
    public async Task SelectBackdrop_MovesTheSelectionAndPersistsIt()
    {
        var fileSystem = new MockFileSystem();
        var settings = CreateSettingsService(fileSystem);
        var viewModel = CreateViewModel(settings);
        // Même dispositif que le thème et la langue : la sauvegarde est un fire-and-forget
        // déclenché par le changement de propriété, guetté par l'évènement.
        var saved = new TaskCompletionSource();
        settings.Changed += (_, _) => saved.TrySetResult();

        viewModel.BackdropChoices.Single(choice => choice.Key == "crystal-vein").SelectCommand.Execute(null);

        viewModel.SelectedBackdropKey.ShouldBe("crystal-vein");
        viewModel.BackdropChoices.Single(choice => choice.IsSelected).Key.ShouldBe("crystal-vein");
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        settings.Current.Backdrop.ShouldBe("crystal-vein");
    }

    [Fact]
    public async Task SelectBackdrop_SurvivesAFreshLoadOfTheSettings()
    {
        var fileSystem = new MockFileSystem();
        var settings = CreateSettingsService(fileSystem);
        var viewModel = CreateViewModel(settings);
        var saved = new TaskCompletionSource();
        settings.Changed += (_, _) => saved.TrySetResult();

        viewModel.BackdropChoices.Single(choice => choice.Key == "reading-room").SelectCommand.Execute(null);
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        // « Relecture » : un second service sur le MÊME fichier factice, comme au redémarrage réel.
        var reloaded = CreateSettingsService(fileSystem);
        await reloaded.LoadAsync();

        reloaded.Current.Backdrop.ShouldBe("reading-room");
        CreateViewModel(reloaded).SelectedBackdropKey.ShouldBe("reading-room");
    }

    [Fact]
    public async Task Constructor_UnknownPersistedBackdrop_SelectsTheDefault()
    {
        // Le repli de Normalized() vu depuis l'écran : une clé fantaisiste ne laisse pas la grille
        // sans sélection.
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Paths.SettingsFilePath, new MockFileData("""
        { "schemaVersion": 1, "theme": "Dark", "language": "fr", "backdrop": "aurora-plateau" }
        """));
        var settings = CreateSettingsService(fileSystem);
        await settings.LoadAsync();

        var viewModel = CreateViewModel(settings);

        viewModel.SelectedBackdropKey.ShouldBe(BackdropCatalog.Default);
        viewModel.BackdropChoices.Count(choice => choice.IsSelected).ShouldBe(1);
    }
}