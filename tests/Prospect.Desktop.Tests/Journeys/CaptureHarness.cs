using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
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
}

internal static class CaptureExtensions
{
    public static T ShouldBeOfTypeForCapture<T>(this object value) => (T)value;
}