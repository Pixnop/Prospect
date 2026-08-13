using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Shell;

using Shouldly;

namespace Prospect.Desktop.Tests.Support;

/// <summary>
/// Captures des écrans touchés par la septième salve de retours de terrain.
/// </summary>
/// <remarks>
/// Même dispositif que <see cref="PlanDialogCapture"/>, et même statut : ce n'est PAS une garde.
/// Rien n'est comparé à une référence et le test réussit tant que le rendu ne lève pas. Il existe
/// pour produire les images d'une revue sans démarrer l'application, et ne s'exécute que si
/// <c>PROSPECT_CAPTURE</c> désigne un dossier d'écriture. Ce que ces écrans doivent GARANTIR est
/// vérifié ailleurs, par des mesures : <c>ModsHeadlessTests</c> pour la visibilité entière des
/// pastilles, <c>ResponsiveRegressionTests</c> pour les boîtes de l'accueil et du détail.
/// </remarks>
public sealed class FieldReportCapture
{
    private static string? OutputDirectory => Environment.GetEnvironmentVariable("PROSPECT_CAPTURE");

    private static void Save(MainWindow window, string directory, string name)
    {
        window.Settle();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.Settle();

        Directory.CreateDirectory(directory);
        var frame = (WriteableBitmap?)window.CaptureRenderedFrame();
        frame.ShouldNotBeNull();
        frame.Save(Path.Combine(directory, $"{name}.png"));
    }

    /// <summary>
    /// Le navigateur de mods avec une carte déjà installée : les pastilles y sont entières ou
    /// absentes, jamais coupées en plein mot. Les deux thèmes, parce que c'est le point de la salve.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public async Task ModBrowserCardBadges(string theme)
    {
        if (OutputDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        var window = ResponsiveScenario.ShowWindow(provider, theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark);
        window.Width = 1280;
        window.Height = 800;

        var shell = provider.GetRequiredService<ShellViewModel>();
        var slug = await provider.SeedTargetInstanceAsync(ResponsiveScenario.LongInstanceName);
        ResponsiveScenario.SeedInstalledMod(provider, fileSystem, slug);

        shell.ShowModBrowser(slug);
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);

        Save(window, directory, $"mod-browser-badges-{theme}-1280x800");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        window.Close();
    }

    /// <summary>L'accueil sans le bouton « Importer un modpack ».</summary>
    [AvaloniaFact]
    public async Task HomeWithoutModpackImport()
    {
        if (OutputDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.22.6");

        var window = ResponsiveScenario.ShowWindow(provider);
        window.Width = 1280;
        window.Height = 800;

        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.22.6");
        await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.ShortInstanceName, "1.22.6");
        shell.LibraryNavItems[0].SelectCommand.Execute(null);
        await home.RefreshCommand.ExecuteAsync(null);

        Save(window, directory, "home-without-modpack-dark-1280x800");
        window.Close();
    }

    /// <summary>Le détail d'instance sans l'action « Exporter ».</summary>
    [AvaloniaFact]
    public async Task InstanceDetailWithoutExport()
    {
        if (OutputDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.22.6");

        var window = ResponsiveScenario.ShowWindow(provider);
        window.Width = 1280;
        window.Height = 800;

        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.22.6");

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);

        Save(window, directory, "instance-detail-without-export-dark-1280x800");
        window.Close();
    }
}