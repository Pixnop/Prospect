using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.ViewModels.Common;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.Views.Common;
using Prospect.Desktop.Views.Home;
using Prospect.Desktop.Views.Mods;

using Shouldly;

namespace Prospect.Desktop.Tests.Shell;

/// <summary>
/// Le shell s'instancie avec le conteneur DI réel sur un système de fichiers factice
/// (docs/architecture.md, exigence de test) et la navigation change effectivement de page.
/// </summary>
public class ShellHeadlessTests
{
    [AvaloniaFact]
    public void MainWindow_ResolvedViaRealDI_ShowsShellWithHomeActiveByDefault()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shellViewModel = provider.GetRequiredService<ShellViewModel>();

        window.Show();
        window.Settle();

        window.GetVisualDescendants().OfType<HomeView>().ShouldNotBeEmpty();
        shellViewModel.CurrentPage.ShouldBe(shellViewModel.Home);
        shellViewModel.LibraryNavItems[0].IsActive.ShouldBeTrue();
        shellViewModel.LibraryNavItems[1].IsActive.ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public void SelectingModsNavItem_ShowsTheModBrowserAndUpdatesActiveState()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shellViewModel = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        window.Settle();

        var modsNavItem = shellViewModel.LibraryNavItems[1];
        modsNavItem.SelectCommand.Execute(null);
        window.Settle();

        shellViewModel.CurrentPage.ShouldBeOfType<ModBrowserViewModel>();
        modsNavItem.IsActive.ShouldBeTrue();
        shellViewModel.LibraryNavItems[0].IsActive.ShouldBeFalse();
        window.GetVisualDescendants().OfType<ModBrowserView>().ShouldNotBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public void SelectingSettingsThenBackToHome_RestoresHomeAsActivePage()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shellViewModel = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        window.Settle();

        shellViewModel.SettingsNavItem.SelectCommand.Execute(null);
        window.Settle();
        shellViewModel.CurrentPage.ShouldBeOfType<PlaceholderPageViewModel>();

        shellViewModel.LibraryNavItems[0].SelectCommand.Execute(null);
        window.Settle();

        shellViewModel.CurrentPage.ShouldBe(shellViewModel.Home);
        shellViewModel.LibraryNavItems[0].IsActive.ShouldBeTrue();
        shellViewModel.SettingsNavItem.IsActive.ShouldBeFalse();
        window.GetVisualDescendants().OfType<HomeView>().ShouldNotBeEmpty();

        window.Close();
    }

    [AvaloniaFact]
    public void ToggleDownloadsPopover_OpensAndClosesWithoutChangingCurrentPage()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shellViewModel = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        window.Settle();
        var pageBefore = shellViewModel.CurrentPage;

        shellViewModel.ToggleDownloadsPopoverCommand.Execute(null);
        window.Settle();
        shellViewModel.IsDownloadsPopoverOpen.ShouldBeTrue();
        shellViewModel.CurrentPage.ShouldBe(pageBefore);

        shellViewModel.CloseDownloadsPopoverCommand.Execute(null);
        window.Settle();
        shellViewModel.IsDownloadsPopoverOpen.ShouldBeFalse();

        window.Close();
    }
}