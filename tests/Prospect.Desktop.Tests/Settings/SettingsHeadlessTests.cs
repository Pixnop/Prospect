using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Settings;
using Prospect.Core.Settings.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Settings;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.Views.Settings;

using Shouldly;

namespace Prospect.Desktop.Tests.Settings;

/// <summary>
/// L'écran Réglages complet, monté sur le shell réel (docs/architecture.md, exigence de test
/// headless) : les cinq onglets rendent chacun leur propre contenu, et le cycle
/// réglage → application → persistance → relecture du thème fonctionne de bout en bout à travers
/// l'UI, pas seulement au niveau des services (voir <c>ThemeServiceTests</c> pour ce niveau-là).
///
/// Ces tests ne cliquent jamais <c>OpenDataFolderCommand</c> ni <c>OpenGitHubCommand</c> : le
/// conteneur de production câble <c>IExternalUrlOpener</c> sur le vrai <c>IProcessRunner</c>
/// système (aucune substitution factice n'existe pour lui dans <c>CompositionRoot</c>, à la
/// différence des clients HTTP), donc les exécuter ici lancerait un vrai gestionnaire de fichiers
/// sur la machine qui fait tourner la suite. Ce comportement est déjà couvert sans risque par
/// <c>SettingsGameViewModelTests</c>/<c>SettingsAboutViewModelTests</c>, construits à la main avec
/// <c>FakeExternalUrlOpener</c>.
/// </summary>
public sealed class SettingsHeadlessTests
{
    private static void NavigateToSettings(ShellViewModel shell, Window window)
    {
        shell.SettingsNavItem.SelectCommand.Execute(null);
        window.Settle();
    }

    [AvaloniaFact]
    public void ShowSettings_DefaultsToGeneralTabWithVslCard()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        NavigateToSettings(shell, window);

        shell.Settings.SelectedTab.ShouldBe(SettingsTab.General);
        window.GetVisualDescendants().OfType<SettingsView>().ShouldNotBeEmpty();
        window.GetVisualDescendants().OfType<RadioButton>().ShouldNotBeEmpty();

        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(SettingsTab.Game)]
    [InlineData(SettingsTab.Network)]
    [InlineData(SettingsTab.Accounts)]
    [InlineData(SettingsTab.About)]
    public void SelectTab_HidesTheGeneralTabsRadioButtons(SettingsTab tab)
    {
        // Preuve indirecte, mais robuste, que chaque onglet affiche bien SON contenu et pas celui
        // d'un autre : les trois boutons radio du thème n'existent que sous l'onglet Général.
        // Les onglets inactifs restent dans l'arbre visuel (IsVisible="False" sur leur conteneur
        // ne les en retire pas) : IsEffectivelyVisible, qui remonte la chaîne des ancêtres, est le
        // signal qui compte réellement, pas la seule présence dans l'arbre.
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        NavigateToSettings(shell, window);

        shell.Settings.SelectTabCommand.Execute(tab);
        window.Settle();

        shell.Settings.SelectedTab.ShouldBe(tab);
        window.GetVisualDescendants().OfType<RadioButton>().Any(radio => radio.IsEffectivelyVisible).ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public void SelectGameTab_ShowsTheRealDataPath()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var appPaths = provider.GetRequiredService<AppPaths>();
        window.Show();
        NavigateToSettings(shell, window);

        shell.Settings.SelectTabCommand.Execute(SettingsTab.Game);
        window.Settle();

        window.GetVisualDescendants().OfType<SelectableTextBlock>()
            .ShouldContain(text => text.Text == appPaths.RootDirectory);

        window.Close();
    }

    [AvaloniaFact]
    public void SelectNetworkTab_ShowsTheConcurrencySelector()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        NavigateToSettings(shell, window);

        shell.Settings.SelectTabCommand.Execute(SettingsTab.Network);
        window.Settle();

        window.GetVisualDescendants().OfType<ComboBox>()
            .ShouldContain(combo => ReferenceEquals(combo.ItemsSource, shell.Settings.Network.AvailableConcurrencyChoices));

        window.Close();
    }

    [AvaloniaFact]
    public void SelectAboutTab_ShowsTheRealAssemblyVersion()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        NavigateToSettings(shell, window);

        shell.Settings.SelectTabCommand.Execute(SettingsTab.About);
        window.Settle();

        window.GetVisualDescendants().OfType<TextBlock>()
            .ShouldContain(text => text.Text == shell.Settings.About.AppVersion);

        window.Close();
    }

    [AvaloniaFact]
    public async Task ThemeCycle_ChangedFromTheSettingsScreen_AppliesLiveAndSurvivesAFreshLoad()
    {
        // Le cycle complet réglage -> application -> persistance -> relecture, à travers la vraie
        // UI plutôt que directement sur les services (voir ThemeServiceTests pour ce niveau-là).
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);

        // Reproduit l'ordre exact d'App.OnFrameworkInitializationCompleted : charger les réglages
        // puis armer ThemeService AVANT de construire la fenêtre. Sans cet ordre, rien n'écoute
        // SettingsService.Changed et le thème ne bougerait jamais (voir la docstring de ThemeService
        // sur pourquoi ce n'est jamais un effet de bord automatique).
        var settingsService = provider.GetRequiredService<SettingsService>();
        await settingsService.LoadAsync();
        provider.GetRequiredService<ThemeService>().ApplyStartupTheme();

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        NavigateToSettings(shell, window);
        Application.Current!.RequestedThemeVariant.ShouldBe(ThemeVariant.Dark);

        shell.Settings.General.SelectThemeCommand.Execute(ThemePreference.Light);
        window.Settle();

        Application.Current!.RequestedThemeVariant.ShouldBe(ThemeVariant.Light);

        // « Relecture » : un second SettingsService sur le MÊME fichier factice, comme au
        // redémarrage réel de l'application.
        var appPaths = provider.GetRequiredService<AppPaths>();
        var reloaded = new SettingsService(fileSystem, appPaths, new JsonFileStore(fileSystem), new SettingsMigrationPipeline([]), new FakeUiCulture());
        await reloaded.LoadAsync();

        reloaded.Current.Theme.ShouldBe(ThemePreference.Light);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        window.Close();
    }
}