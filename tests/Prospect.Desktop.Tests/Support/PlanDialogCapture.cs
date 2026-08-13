using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;

using Shouldly;

namespace Prospect.Desktop.Tests.Support;

/// <summary>
/// Capture d'écran du dialogue d'installation à deux volets, dans les deux thèmes et aux deux
/// tailles utiles.
/// </summary>
/// <remarks>
/// Ce n'est pas une garde : rien n'est comparé à une référence, et le test réussit tant que le
/// rendu ne lève pas. Il existe pour produire les images d'une revue de design à la demande, sans
/// avoir à démarrer l'application. Il ne s'exécute donc que si <c>PROSPECT_CAPTURE</c> désigne un
/// dossier d'écriture, exactement comme les étages conformance et live s'activent par variable
/// d'environnement (docs/architecture.md, « Qualité et CI »).
/// </remarks>
public sealed class PlanDialogCapture
{
    private static string? OutputDirectory => Environment.GetEnvironmentVariable("PROSPECT_CAPTURE");

    [AvaloniaTheory]
    [InlineData("dark", 1280d, 800d)]
    [InlineData("light", 1280d, 800d)]
    [InlineData("dark", 960d, 600d)]
    [InlineData("light", 960d, 600d)]
    public async Task InstallPlanDialog(string theme, double width, double height)
    {
        if (OutputDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out var catalogHandler);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        catalogHandler.ModDb.CatalogJson = FakeModDbHandler.CatalogWith(FakeModDbHandler.CarryOnCatalogEntry);
        catalogHandler.ModDb.CompatibleModIds = [1783, 792, 890];
        catalogHandler.ModDb.CarryOnLibGameVersions = ["1.22.0"];

        var window = ResponsiveScenario.ShowWindow(provider, theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark);
        window.Width = width;
        window.Height = height;

        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.21.3");
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        await shell.ModBrowser.Results.Single(entry => entry.Name == "Carry On").InstallCommand.ExecuteAsync(null);

        var dialog = shell.Overlay.Active.ShouldBeOfType<ModInstallPlanDialogViewModel>();
        dialog.ToggleAllReleasesCommand.Execute(null);
        dialog.SelectedRelease = dialog.Releases.Single(release => release.IsDeclaredIncompatible);
        await dialog.ReloadCompletion;
        window.Settle();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.Settle();

        Directory.CreateDirectory(directory);
        var frame = (WriteableBitmap?)window.CaptureRenderedFrame();
        frame.ShouldNotBeNull();
        frame.Save(Path.Combine(directory, $"install-plan-{theme}-{width:0}x{height:0}.png"));

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        window.Close();
    }
}