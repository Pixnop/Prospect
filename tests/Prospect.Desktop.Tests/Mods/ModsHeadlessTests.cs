using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Instances;
using Prospect.Core.ModDb;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Wizard;
using Prospect.Desktop.Views.Mods;

using Shouldly;

namespace Prospect.Desktop.Tests.Mods;

/// <summary>
/// Écrans de mods montés dans le shell réel, avec le conteneur de production sur système de
/// fichiers et réseau factices (docs/architecture.md, exigence de test headless).
/// </summary>
public sealed class ModsHeadlessTests
{
    private static async Task<InstanceRecord> CreateInstanceAsync(ShellViewModel shell, HomeViewModel home, string name, string version)
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
    public async Task ModBrowser_LoadsAndRendersItsResults()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        shell.CurrentPage.ShouldBeOfType<ModBrowserViewModel>();
        window.GetVisualDescendants().OfType<ModBrowserView>().ShouldNotBeEmpty();
        shell.ModBrowser.Results.ShouldNotBeEmpty();
    }

    [AvaloniaFact]
    public async Task ModBrowser_ModDbUnreachable_RendersTheOfflineBanner()
    {
        using var provider = TestServiceProviderFactory.Create(out _, out var handler);
        handler.ModDb.IsOnline = false;
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        shell.ModBrowser.IsOffline.ShouldBeTrue();
        shell.ModBrowser.ShowEmptyState.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task ModDetailDialog_OpensOverTheBrowser()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        await shell.ModBrowser.Results.Single(card => card.Name == "Config lib").OpenCommand.ExecuteAsync(null);
        window.Settle();

        shell.Overlay.Active.ShouldBeOfType<ModDetailDialogViewModel>();
        window.GetVisualDescendants().OfType<ModDetailDialogView>().ShouldNotBeEmpty();
    }

    [AvaloniaFact]
    public async Task InstanceModsTab_RendersTheRealListInsteadOfAPlaceholder()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        var record = await CreateInstanceAsync(shell, home, "Homestead", "1.21.3");

        var mods = provider.GetRequiredService<IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(record.Slug),
            "configlib-1.11.1.zip",
            ModDbDoubles.ModInfo("configlib", "Config lib", "1.11.1"));

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        detail.SelectedTab.ShouldBe(InstanceDetailTab.Mods);
        detail.ModsTab.Mods.ShouldHaveSingleItem().Name.ShouldBe("Config lib");
        window.GetVisualDescendants().OfType<InstanceModsTabView>().ShouldNotBeEmpty();
    }

    [AvaloniaFact]
    public async Task BrowseFromTheInstanceTab_OpensTheBrowserPrefilteredOnThatInstance()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        var record = await CreateInstanceAsync(shell, home, "Homestead", "1.21.3");

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);

        detail.ModsTab.BrowseCommand.Execute(null);
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        shell.CurrentPage.ShouldBeOfType<ModBrowserViewModel>();
        shell.ModBrowser.SelectedInstance!.Slug.ShouldBe(record.Slug);
    }
}