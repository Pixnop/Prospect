using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Common;
using Prospect.Core.ModDb;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;

using Shouldly;

namespace Prospect.Desktop.Tests.Support;

/// <summary>
/// Captures des trois écrans touchés par la cinquième salve de retours de terrain.
/// </summary>
/// <remarks>
/// Même dispositif que <see cref="PlanDialogCapture"/>, et même statut : ce n'est PAS une garde.
/// Rien n'est comparé à une référence et le test réussit tant que le rendu ne lève pas. Il existe
/// pour produire les images d'une revue sans démarrer l'application, et ne s'exécute que si
/// <c>PROSPECT_CAPTURE</c> désigne un dossier d'écriture. Ce que ces écrans doivent GARANTIR est
/// vérifié ailleurs, par des mesures : <c>TagRowScrollTests</c> pour la rangée de catégories,
/// <c>LogsViewModelTests</c> pour la page Journaux, <c>ModInstallPlanDialogViewModelTests</c> pour
/// le plan.
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

    /// <summary>La rangée de catégories, à l'échelle réelle du vocabulaire, avec sa barre bien placée.</summary>
    [AvaloniaFact]
    public async Task TagRow()
    {
        if (OutputDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        using var provider = TestServiceProviderFactory.Create(out _, out var handler);
        handler.ModDb.CatalogJson = LargeCatalog.CatalogJson(8_000);
        handler.ModDb.TagsJson = LargeCatalog.TagsJson(223);

        var window = ResponsiveScenario.ShowWindow(provider);
        window.Width = 1280;
        window.Height = 800;

        var shell = provider.GetRequiredService<ShellViewModel>();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);

        Save(window, directory, "tag-row-dark-1280x800");
        window.Close();
    }

    /// <summary>La page Journaux, peuplée des faits qu'une session écrit désormais.</summary>
    [AvaloniaFact]
    public async Task LogsPage()
    {
        if (OutputDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out var catalogHandler);
        provider.SeedInstalledVersion(fileSystem, "1.22.6");

        var window = ResponsiveScenario.ShowWindow(provider);
        window.Width = 1280;
        window.Height = 800;

        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.22.6");

        // Des faits variés, écrits par les vrais services quand c'est possible et à la main pour
        // ceux qu'un test headless ne peut pas produire (un jeu qui démarre, un disque qui refuse).
        var log = provider.GetRequiredService<IAppLog>();
        log.Write(AppLogLevel.Info, "Catalogue du jeu actualisé : 214 version(s).");
        log.Write(AppLogLevel.Info, "Téléchargement démarré : « vs_client_linux-x64_1.22.6.tar.gz » depuis cdn.vintagestory.at.");
        log.Write(AppLogLevel.Info, "Téléchargement terminé : « vs_client_linux-x64_1.22.6.tar.gz », 619118592 octets.");
        log.Write(AppLogLevel.Info, "Version 1.22.6 installée : « /home/leon/.local/share/prospect/versions/1.22.6/Vintagestory » présent.");
        log.Write(AppLogLevel.Info, $"Mod installé : « carryon » 2.0.0-pre.8 dans « {record.Slug} ».");
        log.Write(AppLogLevel.Info, $"Mod installé : « carryonlib » 1.0.0-pre.8 dans « {record.Slug} », sans compatibilité déclarée.");
        log.Write(AppLogLevel.Info, $"Jeu lancé : instance « {record.Slug} » en 1.22.6, pid 31842, exécutable « /home/leon/.local/share/prospect/versions/1.22.6/Vintagestory ».");
        log.Write(AppLogLevel.Info, $"Mises à jour : vérification de « {record.Slug} » demandée.");
        log.Write(AppLogLevel.Info, $"Mises à jour : « {record.Slug} » en 1.22.6, 3 mod(s) : 0 à jour disponible, 1 plus récent non déclaré, 2 à jour, 0 inconnu du ModDB, 0 illisible.");
        log.Write(AppLogLevel.Warning, $"Jeu quitté : instance « {record.Slug} », pid 31842, code 1, après 4127 s.");
        log.Write(AppLogLevel.Error, "Erreur montrée à l'utilisateur : Vérification impossible : le ModDB n'a pas répondu.");

        shell.LogsNavItem.SelectCommand.Execute(null);

        Save(window, directory, "logs-page-dark-1280x800");
        window.Close();
    }

    /// <summary>
    /// Le plan d'un mod dont AUCUNE release n'est déclarée compatible : ouvert d'emblée sur toutes
    /// les versions, avec son avertissement. Les deux thèmes, parce que c'est l'écran de la salve
    /// dont la parité claire compte le plus.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public async Task PlanWithoutAnyCompatibleRelease(string theme)
    {
        if (OutputDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out var catalogHandler);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        catalogHandler.ModDb.CatalogJson = FakeModDbHandler.CatalogWith(FakeModDbHandler.CarryOnCatalogEntry);
        catalogHandler.ModDb.CompatibleModIds = [1783, 792, 890];

        // La topologie du terrain : une fiche bien publiée dont rien ne touche la série de l'instance.
        catalogHandler.ModDb.CarryOnLibGameVersions = ["1.19.8"];

        var window = ResponsiveScenario.ShowWindow(provider, theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark);
        window.Width = 1280;
        window.Height = 800;

        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        var record = await ResponsiveScenario.CreateInstanceAsync(shell, home, ResponsiveScenario.LongInstanceName, "1.21.3");

        // Le chemin du docteur d'instance : un mod désigné par son modid textuel, pas par une carte.
        var install = provider.GetRequiredService<ModInstallService>();
        var plan = await install.PrepareAsync(record.Slug, "carryonlib", cancellationToken: CancellationToken.None);

        var dialog = new ModInstallPlanDialogViewModel(
            plan,
            record.Metadata.Name,
            (_, _) => Task.CompletedTask,
            releaseId => install.PrepareAsync(record.Slug, "carryonlib", releaseId: releaseId, cancellationToken: CancellationToken.None),
            shell.Overlay,
            new FakeExternalUrlOpener(),
            new FakeModLogoCache());

        dialog.ShowsAllReleases.ShouldBeTrue();
        dialog.ShowIncompatibleWarning.ShouldBeTrue();
        shell.Overlay.Show(dialog);

        Save(window, directory, $"plan-no-compatible-release-{theme}-1280x800");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        window.Close();
    }
}