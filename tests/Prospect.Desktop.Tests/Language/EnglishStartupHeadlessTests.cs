using System.IO.Abstractions.TestingHelpers;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Settings;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.Support;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Settings;
using Prospect.Desktop.ViewModels.Shell;

using Shouldly;

namespace Prospect.Desktop.Tests.Language;

/// <summary>
/// Le démarrage en anglais, par le VRAI chemin : un <c>prospect.json</c> qui dit <c>"en"</c> est
/// posé sur le système de fichiers factice AVANT que quoi que ce soit ne se construise, les
/// réglages sont lus, la langue appliquée, et seulement ensuite la fenêtre est résolue. C'est
/// exactement l'ordre d'<c>App.OnFrameworkInitializationCompleted</c>.
///
/// Chaque test rétablit le français en sortant (<see cref="UiLanguageScope"/>) : la langue est un
/// état global du processus, au même titre que la variante de thème que les tests de thème clair
/// reposent déjà.
/// </summary>
public sealed class EnglishStartupHeadlessTests
{
    private static ServiceProvider CreateEnglishProvider(out MockFileSystem fileSystem)
    {
        var provider = TestServiceProviderFactory.Create(out fileSystem);
        var paths = provider.GetRequiredService<AppPaths>();

        fileSystem.AddFile(paths.SettingsFilePath, new MockFileData("""
        { "schemaVersion": 1, "theme": "Dark", "language": "en" }
        """));

        provider.GetRequiredService<SettingsService>().LoadAsync().GetAwaiter().GetResult();

        return provider;
    }

    private static string[] VisibleTexts(Window window)
        => window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text))
            .Select(block => block.Text!)
            .ToArray();

    [AvaloniaFact]
    public void Startup_WithEnglishPersisted_ShowsEnglishInTheShell()
    {
        using var provider = CreateEnglishProvider(out _);
        using var language = UiLanguageScope.ApplyStartupLanguage(provider);

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        window.Settle();

        // Moitié XAML : l'en-tête de section de la barre latérale vient du dictionnaire.
        VisibleTexts(window).ShouldContain("Library");

        // Moitié C# : les entrées de navigation sont construites par ShellViewModel.
        shell.LibraryNavItems.Select(item => item.Label).ShouldBe(["Home", "Mods", "Versions"]);
        shell.SettingsNavItem.Label.ShouldBe("Settings");

        window.Close();
    }

    [AvaloniaFact]
    public void Startup_WithEnglishPersisted_ShowsEnglishOnTheHomeScreen()
    {
        using var provider = CreateEnglishProvider(out _);
        using var language = UiLanguageScope.ApplyStartupLanguage(provider);

        var window = provider.GetRequiredService<MainWindow>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        home.RefreshCommand.Execute(null);
        window.Settle();

        var texts = VisibleTexts(window);
        texts.ShouldContain("No instances");
        texts.ShouldContain("An instance keeps a game version, its mods and its saves to itself. Create your first one to get started.");

        window.Close();
    }

    [AvaloniaFact]
    public void Startup_WithEnglishPersisted_ShowsTheRestartHintUnderTheLanguageSelector()
    {
        using var provider = CreateEnglishProvider(out _);
        using var language = UiLanguageScope.ApplyStartupLanguage(provider);

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        shell.SettingsNavItem.SelectCommand.Execute(null);
        window.Settle();

        var texts = VisibleTexts(window);
        texts.ShouldContain("Language");
        texts.ShouldContain("Takes effect the next time you start Prospect.");

        // Le sélecteur reflète bien la langue persistée, et il est modifiable.
        shell.Settings.General.SelectedLanguageIndex.ShouldBe(1);
        shell.Settings.General.IsLanguageEditable.ShouldBeTrue();

        var languageBox = window.GetVisualDescendants().OfType<ComboBox>().First(box => box.ItemCount == 2);
        languageBox.ItemCount.ShouldBe(2);

        window.Close();
    }

    [AvaloniaFact]
    public void Startup_InFrench_ShowsTheRestartHintToo()
    {
        // Le pendant français de la mention : la décision d'architecture est documentée dans l'UI
        // des deux côtés, pas seulement dans la langue qu'on vient d'ajouter.
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        shell.SettingsNavItem.SelectCommand.Execute(null);
        window.Settle();

        VisibleTexts(window).ShouldContain("Le changement prend effet au redémarrage de Prospect.");

        window.Close();
    }

    [AvaloniaFact]
    public async Task Startup_WithEnglishPersisted_ShowsEnglishOnTheInstanceDetail()
    {
        using var provider = CreateEnglishProvider(out var fileSystem);
        using var language = UiLanguageScope.ApplyStartupLanguage(provider);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, "Homestead", "1.20.4");
        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        detail.SelectTabCommand.Execute(InstanceDetailTab.Options);
        window.Settle();

        var texts = VisibleTexts(window);
        texts.ShouldContain("Instance options");
        texts.ShouldContain("Changes take effect at the next launch.");
        // Texte calculé côté C# (jamais lancée), pas une clé de dictionnaire.
        detail.LastPlayedText.ShouldBe("never");

        window.Close();
    }

    [AvaloniaFact]
    public void SettingsGeneral_InEnglish_OffersBothLanguagesInTheirOwnName()
    {
        // Convention tenue des deux côtés : on cherche « English » dans une interface française et
        // « Français » dans une interface anglaise, jamais « Anglais » ou « French ».
        using var provider = CreateEnglishProvider(out _);
        using var language = UiLanguageScope.ApplyStartupLanguage(provider);

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        shell.SettingsNavItem.SelectCommand.Execute(null);
        window.Settle();

        var box = window.GetVisualDescendants().OfType<ComboBox>().First(candidate => candidate.ItemCount == 2);
        box.Items.OfType<ComboBoxItem>().Select(item => item.Content).ShouldBe(["Français", "English"]);

        window.Close();
    }

    [AvaloniaFact]
    public async Task SettingsGeneral_ChoosingFrench_PersistsWithoutRetranslatingTheOpenWindow()
    {
        using var provider = CreateEnglishProvider(out _);
        using var language = UiLanguageScope.ApplyStartupLanguage(provider);

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var settings = provider.GetRequiredService<SettingsService>();
        window.Show();
        shell.SettingsNavItem.SelectCommand.Execute(null);
        window.Settle();

        var saved = new TaskCompletionSource();
        settings.Changed += (_, _) => saved.TrySetResult();
        shell.Settings.General.SelectedLanguageIndex = 0;
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        window.Settle();

        settings.Current.Language.ShouldBe(ProspectSettings.French);
        // La fenêtre ouverte n'a pas bougé : c'est bien un choix qui prend effet au redémarrage.
        VisibleTexts(window).ShouldContain("Language");
        shell.SettingsNavItem.Label.ShouldBe("Settings");

        window.Close();
    }

    [AvaloniaFact]
    public void SettingsGeneral_InEnglish_HoldsItsBoxesAtEverySize()
    {
        // L'anglais est parfois plus long que le français (« Adopt your VS Launcher installs »,
        // « Takes effect the next time you start Prospect. ») : la garde de mise en page tourne donc
        // sur l'écran Réglages, le plus dense en libellés et en champs, aux trois tailles.
        using var provider = CreateEnglishProvider(out _);
        using var language = UiLanguageScope.ApplyStartupLanguage(provider);

        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        window.Settle();

        foreach (var tab in Enum.GetValues<SettingsTab>())
        {
            shell.Settings.SelectTabCommand.Execute(tab);
            window.Settle();
            window.ShouldHoldLayoutInvariantsAtEverySize($"Réglages en anglais, onglet {tab}");
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task InstanceDetail_InEnglish_HoldsItsBoxesAtEverySize()
    {
        // L'autre écran sensible aux longueurs : en-tête, barre d'actions et onglets se disputent
        // la largeur, avec le nom d'instance long du reste de la garde.
        using var provider = CreateEnglishProvider(out var fileSystem);
        using var language = UiLanguageScope.ApplyStartupLanguage(provider);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");

        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.20.4");
        ResponsiveScenario.SeedWorldsAndJournal(provider, fileSystem, record.Slug);
        ResponsiveScenario.SeedInstalledMod(provider, fileSystem, record.Slug);

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        foreach (var tab in Enum.GetValues<InstanceDetailTab>())
        {
            detail.SelectTabCommand.Execute(tab);
            window.Settle();
            window.ShouldHoldLayoutInvariantsAtEverySize($"Détail d'instance en anglais, onglet {tab}");
        }

        window.Close();
    }
}