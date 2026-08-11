using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Versions;
using Prospect.Desktop.Views.Versions;

using Shouldly;

namespace Prospect.Desktop.Tests.Versions;

/// <summary>
/// L'écran Versions s'instancie dans le shell réel (DI complet, système de fichiers en mémoire,
/// réseau factice) et réagit aux filtres ; le popover Téléchargements se branche bien sur la file
/// du DownloadManager (docs/architecture.md, exigences de test headless).
/// </summary>
public class VersionsHeadlessTests
{
    [AvaloniaFact]
    public async Task VersionsScreen_ShowsInstalledAndAvailableVersionsFromTheCatalog()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var versions = provider.GetRequiredService<VersionsViewModel>();

        window.Show();
        shell.LibraryNavItems.First(item => item.Label == "Versions").SelectCommand.Execute(null);
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        window.GetVisualDescendants().OfType<VersionsView>().ShouldNotBeEmpty();
        versions.Installed.Select(row => row.VersionText).ShouldBe(["1.20.4"]);
        versions.Available.Select(row => row.VersionText).ShouldBe(["1.22.0-rc.1", "1.21.3"]);
        versions.SubtitleText.ShouldStartWith("1 installée");

        window.Close();
    }

    [AvaloniaFact]
    public async Task VersionsScreen_HidingUnstable_RemovesTheReleaseCandidateFromTheTree()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var versions = provider.GetRequiredService<VersionsViewModel>();

        window.Show();
        shell.LibraryNavItems.First(item => item.Label == "Versions").SelectCommand.Execute(null);
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        window.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.Text == "1.22.0-rc.1").ShouldBeTrue();

        versions.ShowUnstable = false;
        window.Settle();

        window.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.Text == "1.22.0-rc.1").ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task VersionsScreen_OfflineCatalog_StillShowsWhatIsInstalledAndWarns()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem, out var catalogHandler);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        catalogHandler.IsOnline = false;
        var window = provider.GetRequiredService<MainWindow>();
        var versions = provider.GetRequiredService<VersionsViewModel>();

        window.Show();
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        versions.Installed.Count.ShouldBe(1);
        versions.Available.ShouldBeEmpty();
        versions.CatalogWarning.ShouldNotBeNull();

        window.Close();
    }

    [AvaloniaFact]
    public void DownloadsPopover_WithAnEmptyQueue_KeepsItsEmptyState()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();

        window.Show();
        shell.ToggleDownloadsPopoverCommand.Execute(null);
        window.Settle();

        shell.IsDownloadsPopoverOpen.ShouldBeTrue();
        shell.Downloads.HasDownloads.ShouldBeFalse();
        window.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.Text == "Aucun téléchargement").ShouldBeTrue();

        window.Close();
    }
}