using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Wizard;
using Prospect.Desktop.Views.Home;
using Prospect.Desktop.Views.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests.Home;

/// <summary>
/// L'Accueil affiche l'état vide sans instance, puis une carte après création ; le wizard complet
/// crée réellement une instance de bout en bout sur système de fichiers factice
/// (docs/architecture.md, exigences de test headless).
/// </summary>
public class HomeAndWizardHeadlessTests
{
    [AvaloniaFact]
    public async Task Home_NoInstances_ShowsEmptyStateThenCardAfterWizardCreatesOne()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shellViewModel = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();

        window.Show();
        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        home.HasNoInstancesAtAll.ShouldBeTrue();
        window.GetVisualDescendants().OfType<InstanceCardView>().ShouldBeEmpty();

        home.NewInstanceCommand.Execute(null);
        window.Settle();

        var wizard = shellViewModel.Overlay.Active.ShouldBeOfType<WizardViewModel>();
        window.GetVisualDescendants().OfType<WizardView>().ShouldNotBeEmpty();

        wizard.Name = "Vintage Survival";
        wizard.NextCommand.Execute(null);
        wizard.VersionText = "1.20.4";
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        wizard.IsSummaryStep.ShouldBeTrue();

        await wizard.CreateCommand.ExecuteAsync(null);
        window.Settle();

        shellViewModel.Overlay.Active.ShouldBeNull();
        home.HasNoInstancesAtAll.ShouldBeFalse();
        home.Instances.Count.ShouldBe(1);
        home.Instances[0].Name.ShouldBe("Vintage Survival");
        home.Instances[0].Version.ShouldBe("1.20.4");

        window.GetVisualDescendants().OfType<InstanceCardView>().ShouldNotBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Wizard_CancelBeforeCreating_DoesNotCreateAnyInstance()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shellViewModel = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        home.NewInstanceCommand.Execute(null);
        window.Settle();
        var wizard = shellViewModel.Overlay.Active.ShouldBeOfType<WizardViewModel>();
        wizard.Name = "Abandoned";

        wizard.CancelCommand.Execute(null);
        window.Settle();

        shellViewModel.Overlay.Active.ShouldBeNull();

        await home.RefreshCommand.ExecuteAsync(null);
        home.HasNoInstancesAtAll.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public async Task DeleteInstance_DoubleConfirmation_RemovesCardFromHome()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shellViewModel = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();

        home.NewInstanceCommand.Execute(null);
        window.Settle();
        var wizard = shellViewModel.Overlay.Active.ShouldBeOfType<WizardViewModel>();
        wizard.Name = "Temporaire";
        wizard.NextCommand.Execute(null);
        wizard.VersionText = "1.20.4";
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        await wizard.CreateCommand.ExecuteAsync(null);
        window.Settle();
        home.Instances.Count.ShouldBe(1);

        // Premier clic (menu « Supprimer » de la carte) : ouvre le dialogue de confirmation.
        home.Instances[0].DeleteCommand.Execute(null);
        window.Settle();
        var deleteDialog = shellViewModel.Overlay.Active.ShouldBeOfType<Prospect.Desktop.ViewModels.Dialogs.DeleteInstanceDialogViewModel>();
        deleteDialog.Title.ShouldContain("Temporaire");

        // Second clic (bouton « Supprimer l'instance » du dialogue) : confirmation réelle.
        await deleteDialog.ConfirmCommand.ExecuteAsync(null);
        window.Settle();

        shellViewModel.Overlay.Active.ShouldBeNull();
        home.Instances.ShouldBeEmpty();
        home.HasNoInstancesAtAll.ShouldBeTrue();

        window.Close();
    }
}