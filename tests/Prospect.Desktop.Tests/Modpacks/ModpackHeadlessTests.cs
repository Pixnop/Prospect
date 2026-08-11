using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Instances;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Modpacks;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Wizard;
using Prospect.Desktop.Views.Modpacks;
using Prospect.Desktop.Views.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests.Modpacks;

/// <summary>
/// Export et import de modpack câblés sur le graphe DI réel (docs/architecture.md, exigence de
/// test headless) : <see cref="CompositionRoot"/> tel quel, <see cref="IFilePickerService"/>
/// compris (donc <c>Func&lt;MainWindow&gt;</c>, voir la remarque d'<c>AvaloniaFilePickerService</c>
/// sur le cycle qu'une résolution directe créerait avec <c>ShellViewModel</c>/<c>HomeViewModel</c>).
/// Aucun de ces tests ne va jusqu'au bout d'un vrai sélecteur de fichier : le mode headless n'a pas
/// de fenêtre système à présenter, <see cref="IFilePickerService.PickOpenFileAsync"/> et
/// <see cref="IFilePickerService.PickSaveFileAsync"/> y rendent donc <see langword="null"/>
/// (annulation), ce que les deux ViewModels traitent déjà comme un retour silencieux au
/// formulaire — exactement ce que ces tests vérifient.
/// </summary>
public sealed class ModpackHeadlessTests
{
    private static async Task<InstanceRecord> CreateInstanceViaWizardAsync(ShellViewModel shell, HomeViewModel home, string name, string version)
    {
        home.NewInstanceCommand.Execute(null);
        var wizard = shell.Overlay.Active.ShouldBeOfType<WizardViewModel>();
        await wizard.LoadVersionsCommand.ExecuteAsync(null);
        wizard.Name = name;
        wizard.NextCommand.Execute(null);
        wizard.VersionChoices.First(choice => choice.VersionText == version).SelectCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);

        InstanceRecord? created = null;
        wizard.Created += (_, record) => created = record;
        await wizard.CreateCommand.ExecuteAsync(null);

        return created ?? throw new InvalidOperationException("Le wizard n'a pas créé d'instance.");
    }

    [AvaloniaFact]
    public async Task ExportCommand_OpensTheExportDialogWithTheInstanceName()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        await CreateInstanceViaWizardAsync(shell, home, "Vintage Survival", "1.20.4");
        window.Settle();
        home.Instances[0].OpenCommand.Execute(null);
        window.Settle();
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();

        detail.ExportCommand.Execute(null);
        window.Settle();

        var export = shell.Overlay.Active.ShouldBeOfType<ExportModpackDialogViewModel>();
        export.Title.ShouldContain("Vintage Survival");
        window.GetVisualDescendants().OfType<ExportModpackDialogView>().ShouldNotBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public async Task ExportCommand_PickerCanceled_LeavesTheDialogOnItsForm()
    {
        // Le mode headless n'a pas de fenêtre système : le sélecteur "Enregistrer sous" rend
        // toujours null, exactement comme si l'utilisateur avait annulé.
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        await CreateInstanceViaWizardAsync(shell, home, "Vintage Survival", "1.20.4");
        window.Settle();
        home.Instances[0].OpenCommand.Execute(null);
        window.Settle();
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        detail.ExportCommand.Execute(null);
        window.Settle();
        var export = shell.Overlay.Active.ShouldBeOfType<ExportModpackDialogViewModel>();

        await export.ExportCommand.ExecuteAsync(null);
        window.Settle();

        shell.Overlay.Active.ShouldBeSameAs(export);
        export.IsResultPhase.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task ImportModpackCommand_PickerCanceled_NeverOpensAnOverlay()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        await home.ImportModpackCommand.ExecuteAsync(null);
        window.Settle();

        shell.Overlay.Active.ShouldBeNull();

        window.Close();
    }
}