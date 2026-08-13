using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.ModDb;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Shell;

using Shouldly;

namespace Prospect.Desktop.Tests.Support;

/// <summary>
/// Capture de l'onglet Mods avec les pastilles tirées du journal du dernier lancement : un mod en
/// erreur, un mod en avertissement, et une paire « fonctionne avec ».
/// </summary>
/// <remarks>
/// Même statut que <see cref="PlanDialogCapture"/> et <see cref="FieldReportCapture"/> : ce n'est
/// PAS une garde, rien n'est comparé à une référence, et l'image ne sort que si
/// <c>PROSPECT_CAPTURE</c> désigne un dossier. Ce que cet écran doit garantir est vérifié
/// ailleurs, par des mesures (<c>ResponsiveRegressionTests</c> pour les boîtes,
/// <c>ModsHeadlessTests</c> pour le rendu effectif des pastilles).
/// </remarks>
public sealed class ModLogBadgeCapture
{
    private static string? OutputDirectory => Environment.GetEnvironmentVariable("PROSPECT_CAPTURE");

    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public async Task InstanceModsTabWithLaunchBadges(string theme)
    {
        if (OutputDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");

        var window = ResponsiveScenario.ShowWindow(provider, theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark);
        window.Width = 1280;
        window.Height = 800;

        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.21.3");

        var modsDirectory = provider.GetRequiredService<IInstalledModRepository>().GetModsDirectory(record.Slug);

        // Un mod de transport qui patche le contenu d'un autre mod installé : la paire
        // « fonctionne avec » vient de son archive, sans qu'aucun lancement ne soit nécessaire.
        ModDbDoubles.SeedMod(
            fileSystem,
            modsDirectory,
            "carryon-2.0.0.zip",
            ModDbDoubles.ModInfo("carryon", "Carry On", "2.0.0"),
            new Dictionary<string, string>
            {
                ["assets/carryon/patches/bettercrates.json"] =
                    """[{ "file": "bettercrates:blocktypes/bettercrates", "op": "add", "path": "/behaviors/-", "value": { "name": "Carryable" } }]""",
            });
        ModDbDoubles.SeedMod(fileSystem, modsDirectory, "bettercrates-1.6.0.zip", ModDbDoubles.ModInfo("bettercrates", "Better Crates", "1.6.0"));
        ModDbDoubles.SeedMod(fileSystem, modsDirectory, "hearthside-1.4.2.zip", ModDbDoubles.ModInfo("hearthside", "Hearthside", "1.4.2"));
        ModDbDoubles.SeedMod(fileSystem, modsDirectory, "cartomap-0.9.0.zip", ModDbDoubles.ModInfo("cartomap", "Cartomap", "0.9.0"));

        // Et un journal de lancement qui accable l'un et avertit l'autre.
        fileSystem.AddFile(
            provider.GetRequiredService<Core.Launching.GameLauncher>().GetLogFilePath(record.Slug),
            new System.IO.Abstractions.TestingHelpers.MockFileData(string.Join(
                Environment.NewLine,
                "13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: hearthside, carryon, bettercrates, cartomap, game",
                "13.8.2026 21:08:23 [Client Error] [hearthside] Could not resolve some dependencies:",
                "13.8.2026 21:08:23 [Client Error] [hearthside]     saltyseas - Missing",
                "13.8.2026 21:08:24 [Client Error] Patch 0 in hearthside:patches/kitchen.json: File saltyseas:blocktypes/barrel.json not found",
                "13.8.2026 21:08:25 [Client Warning] [cartomap] tile cache is stale, rebuilding it at first use")));

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);

        window.Settle();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.Settle();

        Directory.CreateDirectory(directory);
        var frame = (WriteableBitmap?)window.CaptureRenderedFrame();
        frame.ShouldNotBeNull();
        frame.Save(Path.Combine(directory, $"instance-mods-log-badges-{theme}-1280x800.png"));

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        window.Close();
    }
}