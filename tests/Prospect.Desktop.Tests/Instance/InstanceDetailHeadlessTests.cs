using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Instances;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Wizard;
using Prospect.Desktop.Views.Instance;
using Prospect.Desktop.Views.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests.Instance;

/// <summary>
/// Navigation vers la page de détail et retour, bascule d'onglets, sauvegarde des Options : le
/// shell réel branché en DI sur système de fichiers factice (docs/architecture.md, exigence de
/// test headless). Ne clique jamais sur Jouer/Arrêter ici : <see cref="GameLauncher"/>, câblé sur
/// le <c>SystemProcessRunner</c> réel via le conteneur de production, spawnerait un vrai
/// processus pour un exécutable qui n'existe que dans le système de fichiers factice — ce
/// comportement est déjà entièrement couvert sans risque par
/// <c>InstanceDetailViewModelTests</c>/<c>InstanceCardViewModelTests</c>, qui construisent
/// <see cref="Prospect.Core.Launching.GameLauncher"/> à la main avec un <c>IProcessRunner</c> factice.
/// </summary>
public sealed class InstanceDetailHeadlessTests
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
    public async Task ClickingACard_OpensTheInstanceDetailPage()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        await CreateInstanceViaWizardAsync(shell, home, "Vintage Survival", "1.20.4");
        window.Settle();
        home.Instances.Count.ShouldBe(1);

        home.Instances[0].OpenCommand.Execute(null);
        window.Settle();

        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        detail.Slug.ShouldBe(home.Instances[0].Slug);
        window.GetVisualDescendants().OfType<InstanceDetailView>().ShouldNotBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public async Task InstanceDetail_InitializesHeaderFromDisk()
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
        detail.Name.ShouldBe("Vintage Survival");
        detail.VersionText.ShouldBe("1.20.4");

        window.Close();
    }

    [AvaloniaFact]
    public async Task InstanceDetail_SelectTab_SwitchesTheSelectedTab()
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
        detail.SelectedTab.ShouldBe(InstanceDetailTab.Mods);

        detail.SelectTabCommand.Execute(InstanceDetailTab.Options);
        window.Settle();

        detail.SelectedTab.ShouldBe(InstanceDetailTab.Options);

        window.Close();
    }

    [AvaloniaFact]
    public async Task InstanceDetail_OptionsTab_Save_PersistsThroughInstanceService()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        var repository = provider.GetRequiredService<IInstanceRepository>();
        window.Show();
        await CreateInstanceViaWizardAsync(shell, home, "Vintage Survival", "1.20.4");
        window.Settle();
        home.Instances[0].OpenCommand.Execute(null);
        window.Settle();
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();

        detail.SelectTabCommand.Execute(InstanceDetailTab.Options);
        detail.OptionsTab.ExtraArgsText = "--dev";
        detail.OptionsTab.EnvVarsText = "FOO=bar";
        await detail.OptionsTab.SaveCommand.ExecuteAsync(null);
        window.Settle();

        var reloaded = await repository.LoadAsync(detail.Slug, CancellationToken.None);
        reloaded.Metadata.Launch.ExtraArgs.ShouldBe(["--dev"]);
        reloaded.Metadata.Launch.Env["FOO"].ShouldBe("bar");

        window.Close();
    }

    [AvaloniaFact]
    public async Task InstanceDetail_Back_ReturnsToHome()
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

        detail.BackCommand.Execute(null);
        window.Settle();

        shell.CurrentPage.ShouldBeSameAs(home);

        window.Close();
    }
}