using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Settings;
using Prospect.Desktop.ViewModels.Shell;

namespace Prospect.Desktop.Tests.Journeys;

/// <summary>
/// Captures d'écran du rendu Skia headless, pour regarder À L'ŒIL ce que les tests mesurent.
/// Opt-in par variable d'environnement : rien ne s'écrit sur disque sans <c>PROSPECT_CAPTURE</c>.
/// </summary>
public sealed class CaptureHarness
{
    private static string? Directory => Environment.GetEnvironmentVariable("PROSPECT_CAPTURE");

    private static void Save(Window window, string name)
    {
        if (Directory is not { } directory)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(directory);
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(System.IO.Path.Combine(directory, name + ".png"));
    }

    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public async Task CaptureDialogOverPage(string variant)
    {
        if (Directory is null)
        {
            return;
        }

        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out var catalogHandler, out _);
        catalogHandler.ModDb.CatalogJson = FakeModDbHandler.CatalogWith(FakeModDbHandler.CarryOnCatalogEntry);
        catalogHandler.ModDb.CompatibleModIds = [1783, 792, 890, 4687];
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var slug = await provider.SeedTargetInstanceAsync("Homestead", "1.21.3");

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Width = 1280;
        window.Height = 800;
        window.Show();
        window.RequestedThemeVariant = variant == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        window.Pump();

        shell.ShowModBrowser(slug);
        var browser = shell.CurrentPage.ShouldBeOfTypeForCapture<ModBrowserViewModel>();
        await browser.InitializeCommand.ExecuteAsync(null);
        window.Pump();
        Save(window, $"browser-{variant}");

        var card = browser.Results.First(entry => entry.Name == "Carry On");
        await card.OpenCommand.ExecuteAsync(null);
        window.Pump();
        Save(window, $"mod-detail-{variant}");

        shell.Overlay.Close();
        await card.InstallCommand.ExecuteAsync(null);
        window.Pump();
        Save(window, $"dialog-{variant}");

        shell.Overlay.Close();
        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfTypeForCapture<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        detail.SelectTabCommand.Execute(InstanceDetailTab.Options);
        window.Pump();
        Save(window, $"options-{variant}");

        window.Close();
    }

    /// <summary>
    /// L'écran d'import VS Launcher, par-dessus les Réglages. Capturé à part parce qu'il demande un
    /// dossier VS Launcher posé sur le disque factice et un passage par la détection : c'est le
    /// chemin réel, pas un dialogue construit à la main.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public async Task CaptureVslImport(string variant)
    {
        if (Directory is null)
        {
            return;
        }

        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out _, out _);
        provider.SeedVslInstallation(fileSystem, "/vsl/installations/survie", "1.20.4");
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        await provider.GetRequiredService<Prospect.Core.Settings.SettingsService>().LoadAsync();

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Width = 1280;
        window.Height = 800;
        window.Show();
        window.RequestedThemeVariant = variant == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        window.Pump();

        shell.SettingsNavItem.SelectCommand.Execute(null);
        var settings = shell.CurrentPage.ShouldBeOfTypeForCapture<SettingsViewModel>();
        await settings.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        // La carte VS Launcher vit sous le sélecteur de fond : sans ce défilement, la capture
        // s'arrête au thème et ne montre pas le texte qu'on vient d'y ajouter.
        var page = window.GetVisualDescendants().OfType<ScrollViewer>()
            .First(view => view.Extent.Height > view.Viewport.Height);
        page.Offset = new Vector(0, page.Extent.Height);
        window.Pump();
        Save(window, $"settings-vsl-card-{variant}");

        settings.OpenAdoptionCommand.Execute(null);
        window.Pump();
        Save(window, $"vsl-import-{variant}");

        window.Close();
    }
}

internal static class CaptureExtensions
{
    public static T ShouldBeOfTypeForCapture<T>(this object value) => (T)value;
}