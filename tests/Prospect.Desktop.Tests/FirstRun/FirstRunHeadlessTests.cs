using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Settings;
using Prospect.Desktop.ViewModels.FirstRun;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Migration;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Versions;
using Prospect.Desktop.Views.FirstRun;

using Shouldly;

namespace Prospect.Desktop.Tests.FirstRun;

/// <summary>
/// L'écran de premier lancement câblé sur le graphe DI réel (docs/architecture.md, exigence de
/// test headless), bout en bout : affichage une seule fois piloté par le réglage persisté, chaque
/// ligne de la checklist branchée sur les vrais services, sortie vers Commencer/Passer/Installer
/// une version/Adopter VS Launcher, et retour depuis les Réglages (« Revoir le premier lancement »).
/// </summary>
public sealed class FirstRunHeadlessTests
{
    [AvaloniaFact]
    public async Task ShowFirstRunIfNeeded_NeverSeenBefore_ShowsTheScreenOnTheModalPanel()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        shell.ShowFirstRunIfNeeded();
        window.Settle();

        var firstRun = shell.Overlay.Active.ShouldBeOfType<FirstRunScreenViewModel>();
        window.GetVisualDescendants().OfType<FirstRunScreenView>().ShouldNotBeEmpty();
        // Ni version installée ni VS Launcher détecté sur ce conteneur de test : dossier de données
        // (toujours satisfait), version du jeu (proposée) et compte (proposé), pas de quatrième
        // entrée.
        firstRun.Steps.Count.ShouldBe(3);

        window.Close();
    }

    [AvaloniaFact]
    public void ShowFirstRunIfNeeded_AlreadySeen_DoesNothing()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var settings = provider.GetRequiredService<SettingsService>();
        window.Show();
        settings.UpdateAsync(current => current with { HasSeenFirstRun = true }).GetAwaiter().GetResult();

        shell.ShowFirstRunIfNeeded();
        window.Settle();

        shell.Overlay.Active.ShouldBeNull();

        window.Close();
    }

    [AvaloniaFact]
    public async Task FullFlow_StartButton_DismissesAndPersistsHasSeenFirstRun()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var settings = provider.GetRequiredService<SettingsService>();
        window.Show();

        shell.ShowFirstRunIfNeeded();
        window.Settle();
        var firstRun = shell.Overlay.Active.ShouldBeOfType<FirstRunScreenViewModel>();

        await firstRun.StartCommand.ExecuteAsync(null);
        window.Settle();

        shell.Overlay.Active.ShouldBeNull();
        settings.Current.HasSeenFirstRun.ShouldBeTrue();
        // Un second passage, comme un vrai redémarrage relisant prospect.json, ne rouvre plus l'écran.
        shell.ShowFirstRunIfNeeded();
        shell.Overlay.Active.ShouldBeNull();

        window.Close();
    }

    [AvaloniaFact]
    public async Task FullFlow_InstallVersionAction_NavigatesToVersionsAndMarksSeen()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var settings = provider.GetRequiredService<SettingsService>();
        window.Show();

        shell.ShowFirstRunIfNeeded();
        window.Settle();
        var firstRun = shell.Overlay.Active.ShouldBeOfType<FirstRunScreenViewModel>();
        var versionStep = firstRun.Steps[1];
        versionStep.HasAction.ShouldBeTrue();
        versionStep.ActionCommand.ShouldBeSameAs(firstRun.GoToVersionsCommand);

        await firstRun.GoToVersionsCommand.ExecuteAsync(null);
        window.Settle();

        shell.Overlay.Active.ShouldBeNull();
        shell.CurrentPage.ShouldBeOfType<VersionsViewModel>();
        settings.Current.HasSeenFirstRun.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task FullFlow_AdoptVslAction_OpensSharedAdoptionDialogAndRefreshesHomeOnCompletion()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/survie", "1.20.4");
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();

        shell.ShowFirstRunIfNeeded();
        window.Settle();
        var firstRun = shell.Overlay.Active.ShouldBeOfType<FirstRunScreenViewModel>();
        firstRun.Steps.Count.ShouldBe(4);
        var vslStep = firstRun.Steps[3];
        vslStep.ActionCommand.ShouldBeSameAs(firstRun.OpenVslAdoptionCommand);

        await firstRun.OpenVslAdoptionCommand.ExecuteAsync(null);
        window.Settle();
        var adopt = shell.Overlay.Active.ShouldBeOfType<AdoptVslViewModel>();

        await adopt.ConfirmCommand.ExecuteAsync(null);
        window.Settle();

        home.Instances.ShouldContain(instance => instance.Name == "Survie médiévale");

        window.Close();
    }

    [AvaloniaFact]
    public async Task ReplayFirstRun_FromSettings_ReopensTheScreenAfterItWasAlreadySeen()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        shell.ShowFirstRunIfNeeded();
        window.Settle();
        var firstRun = shell.Overlay.Active.ShouldBeOfType<FirstRunScreenViewModel>();
        await firstRun.SkipCommand.ExecuteAsync(null);
        window.Settle();
        shell.Overlay.Active.ShouldBeNull();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        window.Settle();
        await shell.Settings.ReplayFirstRunCommand.ExecuteAsync(null);
        window.Settle();

        shell.Overlay.Active.ShouldBeSameAs(firstRun);
        window.GetVisualDescendants().OfType<FirstRunScreenView>().ShouldNotBeEmpty();

        window.Close();
    }
}