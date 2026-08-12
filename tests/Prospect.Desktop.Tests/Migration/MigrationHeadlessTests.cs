using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Migration;
using Prospect.Desktop.ViewModels.Settings;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.Views.Migration;
using Prospect.Desktop.Views.Settings;

using Shouldly;

namespace Prospect.Desktop.Tests.Migration;

/// <summary>
/// Le flux d'adoption VS Launcher câblé sur le graphe DI réel (docs/architecture.md, exigence de
/// test headless), bout en bout : détection au démarrage de l'Accueil vide, ouverture depuis la
/// carte de rappel ET depuis les Réglages, sélection, adoption, rapport, l'instance apparaît à
/// l'Accueil.
/// </summary>
public sealed class MigrationHeadlessTests
{
    [AvaloniaFact]
    public async Task Home_EmptyWithNothingDetected_NeverShowsTheFirstRunCard()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();

        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        home.HasNoInstancesAtAll.ShouldBeTrue();
        home.FirstRun.VslDetected.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Home_EmptyWithVslDetected_ShowsTheFirstRunCardAndOpensAdoption()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/survie", "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();

        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        home.FirstRun.VslDetected.ShouldBeTrue();

        home.FirstRun.OpenAdoptionCommand.Execute(null);
        window.Settle();

        var adopt = shell.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();
        window.GetVisualDescendants().OfType<AdoptVslView>().ShouldNotBeEmpty();
        adopt.InstallationRows.ShouldHaveSingleItem().Name.ShouldBe("Survie médiévale");

        window.Close();
    }

    [AvaloniaFact]
    public async Task FullFlow_FromFirstRunCardToHome_CreatesTheInstanceAndUpdatesTheGrid()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/survie", "1.20.4");
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        home.FirstRun.OpenAdoptionCommand.Execute(null);
        window.Settle();
        var adopt = shell.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();

        await adopt.ConfirmCommand.ExecuteAsync(null);
        window.Settle();

        adopt.Step.ShouldBe(AdoptVslStep.Report);
        home.Instances.ShouldContain(instance => instance.Name == "Survie médiévale");

        adopt.CloseReportCommand.Execute(null);
        window.Settle();
        shell.Overlay.Active.ShouldBeNull();

        window.Close();
    }

    [AvaloniaFact]
    public async Task SettingsNavItem_ShowsTheSettingsPageAndDetectsAtDefaultPath()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/survie", "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        window.Settle();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        window.Settle();

        var settings = shell.CurrentPage.ShouldBeOfType<SettingsViewModel>();
        window.GetVisualDescendants().OfType<SettingsView>().ShouldNotBeEmpty();
        settings.VslDetected.ShouldBeTrue();

        settings.OpenAdoptionCommand.Execute(null);
        window.Settle();
        shell.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Settings_PickFolderPickerCanceled_LeavesDetectionUnchanged()
    {
        // Le mode headless n'a pas de fenêtre système : le sélecteur de dossier rend toujours null,
        // exactement comme si l'utilisateur avait annulé (même remarque que ModpackHeadlessTests).
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        shell.SettingsNavItem.SelectCommand.Execute(null);
        window.Settle();
        var settings = shell.CurrentPage.ShouldBeOfType<SettingsViewModel>();

        await settings.PickFolderCommand.ExecuteAsync(null);
        window.Settle();

        settings.VslDetected.ShouldBeFalse();

        window.Close();
    }
}