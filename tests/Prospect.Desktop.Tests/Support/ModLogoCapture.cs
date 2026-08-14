using System.IO.Abstractions.TestingHelpers;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;

using Shouldly;

namespace Prospect.Desktop.Tests.Support;

/// <summary>
/// Captures des surfaces où le logo d'un mod vient d'apparaître : l'onglet Mods d'une instance, avec
/// des mods venus du ModDB à côté d'un mod déposé à la main, et le dialogue d'installation avec sa
/// dépendance.
/// </summary>
/// <remarks>
/// Même dispositif que <see cref="PlanDialogCapture"/> : ce n'est pas une garde, rien n'est comparé
/// à une référence, et le test ne s'exécute que si <c>PROSPECT_CAPTURE</c> désigne un dossier
/// d'écriture. Il existe pour montrer le rendu sans démarrer l'application.
/// </remarks>
public sealed class ModLogoCapture
{
    private static string? OutputDirectory => Environment.GetEnvironmentVariable("PROSPECT_CAPTURE");

    /// <summary>Fiche de catalogue de CarryOnLib, avec un logo : la dépendance du dialogue d'installation.</summary>
    private const string CarryOnLibCatalogEntry = """
    { "modid": 4687, "assetid": 27960, "downloads": 812000, "follows": 3100, "trendingpoints": 4, "comments": 12,
      "name": "CarryOnLib", "summary": "Bibliothèque partagée de Carry On.", "modidstrs": ["carryonlib"],
      "author": "NerdScurvy", "urlalias": "carryonlib", "side": "both", "type": "mod",
      "logo": "https://moddbcdn.vintagestory.at/carryonlib.png",
      "tags": ["Library"], "lastreleased": "2026-08-01 09:00:00" }
    """;

    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public async Task InstanceModsTab(string theme)
    {
        if (OutputDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        using var provider = Prepare(out var fileSystem, out var shell, out var slug);
        var window = ResponsiveScenario.ShowWindow(provider, theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark);
        window.Width = 1280;
        window.Height = 800;

        // Deux mods venus du ModDB, chacun avec son logo, et un troisième déposé à la main qui garde
        // le pictogramme : c'est le contraste que cette capture doit montrer.
        ResponsiveScenario.SeedModDbMod(provider, fileSystem, slug, "betterruins", "BetterRuins", modDbModId: 792, version: "2.0.0");
        ResponsiveScenario.SeedModDbMod(provider, fileSystem, slug, "carryon", "Carry On", modDbModId: 890, version: "2.0.0");
        ModDbDoubles.SeedMod(
            fileSystem,
            provider.GetRequiredService<Core.ModDb.IInstalledModRepository>().GetModsDirectory(slug),
            "extrainfo-1.4.0.zip",
            ModDbDoubles.ModInfo("extrainfo", "Extra Info", "1.4.0"));

        await WarmCatalogAsync(shell);
        var detail = await OpenModsTabAsync(shell, slug, window);
        foreach (var row in detail.ModsTab.Mods)
        {
            await row.Thumbnail.LoadCompletion;
        }

        Save(window, directory, $"instance-mods-tab-{theme}.png");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public async Task InstallPlanDialog(string theme)
    {
        if (OutputDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        using var provider = Prepare(out _, out var shell, out var slug);
        var window = ResponsiveScenario.ShowWindow(provider, theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark);
        window.Width = 1280;
        window.Height = 800;

        await WarmCatalogAsync(shell);
        await shell.ModBrowser.Results.Single(card => card.Name == "Carry On").InstallCommand.ExecuteAsync(null);

        var dialog = shell.Overlay.Active.ShouldBeOfType<ModInstallPlanDialogViewModel>();
        await dialog.Thumbnail.LoadCompletion;
        foreach (var dependency in dialog.Dependencies.Concat(dialog.InstallableAnyway))
        {
            await dependency.Thumbnail.LoadCompletion;
        }

        Save(window, directory, $"install-plan-dialog-{theme}.png");
        window.Close();
    }

    // Catalogue réel dans sa forme utile ici : trois fiches à logo (BetterRuins, Carry On,
    // CarryOnLib) plus une sans (Config lib), chaque logo servi d'une couleur différente pour qu'on
    // les distingue à l'œil sur la capture.
    private static ServiceProvider Prepare(out MockFileSystem fileSystem, out ShellViewModel shell, out string slug)
    {
        var provider = ResponsiveScenario.CreateProvider(out fileSystem, out var catalogHandler);
        catalogHandler.ModDb.CatalogJson = FakeModDbHandler.CatalogWith(
            FakeModDbHandler.CarryOnCatalogEntry,
            CarryOnLibCatalogEntry);
        catalogHandler.ModDb.CompatibleModIds = [1783, 792, 890, 4687];
        catalogHandler.ModDb.LogoBytesFor = LogoFor;

        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var created = provider.GetRequiredService<Core.Instances.InstanceService>()
            .CreateAsync("Homestead 1.21", Core.Common.GameVersion.Parse("1.21.3"))
            .GetAwaiter()
            .GetResult();
        slug = created.Slug;
        shell = provider.GetRequiredService<ShellViewModel>();
        provider.GetRequiredService<HomeViewModel>();

        return provider;
    }

    private static byte[] LogoFor(Uri url) => url.AbsolutePath switch
    {
        "/betterruins.png" => TinyPng.Create(96, 0x6F, 0x9E, 0x5C),
        "/CarryOnLogo.png" => TinyPng.Create(96, 0xC4, 0x85, 0x4F),
        "/carryonlib.png" => TinyPng.Create(96, 0x5C, 0x82, 0x9E),
        _ => TinyPng.Create(96, 0x8A, 0x6F, 0x9E),
    };

    private static async Task WarmCatalogAsync(ShellViewModel shell)
    {
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        foreach (var card in shell.ModBrowser.Results)
        {
            await card.LogoLoadCompletion;
        }
    }

    private static async Task<InstanceDetailViewModel> OpenModsTabAsync(ShellViewModel shell, string slug, Window window)
    {
        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        detail.SelectTabCommand.Execute(InstanceDetailTab.Mods);
        window.Settle();

        return detail;
    }

    private static void Save(Window window, string directory, string fileName)
    {
        window.Settle();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.Settle();

        Directory.CreateDirectory(directory);
        var frame = (WriteableBitmap?)window.CaptureRenderedFrame();
        frame.ShouldNotBeNull();
        frame.Save(Path.Combine(directory, fileName));

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
    }
}