using System.IO.Abstractions.TestingHelpers;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Launching;
using Prospect.Core.ModDb;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests.Support;

/// <summary>
/// Fabrique de scénarios pour la garde de mise en page : monte le shell réel sur le conteneur DI
/// de production (système de fichiers et réseau factices, comme les autres tests headless), le
/// peuple d'un jeu de données volontairement hostile aux boîtes trop justes, puis inspecte la
/// fenêtre à chaque taille de la garde.
///
/// Les valeurs sont choisies pour tendre la mise en page, pas pour la flatter : nom d'instance
/// long, version avec suffixe de canal, chemin de dataPath complet. Un scénario qui tient avec des
/// valeurs courtes ne prouve rien.
/// </summary>
internal static class ResponsiveScenario
{
    /// <summary>
    /// Nom d'instance long : c'est lui qui pousse l'en-tête de la page de détail contre la barre
    /// d'actions, et le nom de carte contre les bords de la vignette.
    /// </summary>
    public const string LongInstanceName = "Survie médiévale sur serveur communautaire";

    public const string ShortInstanceName = "Bac à sable";

    public static ServiceProvider CreateProvider(out MockFileSystem fileSystem, out FakeCatalogHandler catalogHandler)
        => TestServiceProviderFactory.Create(out fileSystem, out catalogHandler);

    /// <summary>
    /// Résout la fenêtre principale et l'affiche sous la variante de thème demandée. La variante
    /// se pose AVANT <see cref="Window.Show"/> : posée après, les ressources déjà résolues à la
    /// construction des contrôles ne sont pas toutes réévaluées, et le test mesurerait une fenêtre
    /// à moitié basculée.
    /// </summary>
    public static MainWindow ShowWindow(ServiceProvider provider, ThemeVariant? theme = null)
    {
        Application.Current!.RequestedThemeVariant = theme ?? ThemeVariant.Dark;
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        window.Settle();

        return window;
    }

    /// <summary>Crée une instance en traversant le wizard, comme le ferait l'utilisateur.</summary>
    public static async Task<InstanceRecord> CreateInstanceAsync(
        ShellViewModel shell,
        HomeViewModel home,
        string name,
        string version)
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

    /// <summary>
    /// Pose sur le système de fichiers factice de quoi remplir les onglets Mondes et Journal :
    /// sans ça, la page de détail n'affiche que ses états vides, et la moitié de sa mise en page
    /// n'est jamais mesurée.
    /// </summary>
    public static void SeedWorldsAndJournal(ServiceProvider provider, MockFileSystem fileSystem, string slug)
    {
        var repository = provider.GetRequiredService<IInstanceRepository>();
        var saves = fileSystem.Path.Combine(repository.GetDataDirectory(slug), "Saves");
        fileSystem.AddFile(
            fileSystem.Path.Combine(saves, "survie-communautaire-hiver-1204.vcdbs"),
            new MockFileData(new byte[4096]));
        fileSystem.AddFile(fileSystem.Path.Combine(saves, "bac-a-sable.vcdbs"), new MockFileData(new byte[512]));

        var launcher = provider.GetRequiredService<GameLauncher>();
        fileSystem.AddFile(
            launcher.GetLogFilePath(slug),
            new MockFileData(string.Join(
                Environment.NewLine,
                Enumerable.Range(0, 12).Select(line => $"[Server Event] 12:0{line % 10}:11 [Notification] Chargement du monde, étape {line} sur 12"))));
    }


    /// <summary>Installe un mod dans l'instance, pour que l'onglet Mods ait une vraie ligne à mesurer.</summary>
    public static void SeedInstalledMod(ServiceProvider provider, MockFileSystem fileSystem, string slug)
    {
        var mods = provider.GetRequiredService<IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(slug),
            "configlib-1.11.1.zip",
            ModDbDoubles.ModInfo("configlib", "Config lib", "1.11.1"));
    }

    /// <summary>
    /// Pose un journal de lancement qui accable le mod <paramref name="modId"/> et lui donne une
    /// intégration manquante : c'est ce qui fait apparaître les pastilles du dernier lancement,
    /// donc la rangée la plus large que la ligne de mod puisse avoir à porter.
    /// </summary>
    public static void SeedLaunchLogBlaming(ServiceProvider provider, MockFileSystem fileSystem, string slug, string modId)
    {
        var launcher = provider.GetRequiredService<GameLauncher>();
        var lines = string.Join(
            Environment.NewLine,
            $"13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: {modId}, game",
            $"13.8.2026 21:08:23 [Client Error] [{modId}] Could not resolve some dependencies:",
            $"13.8.2026 21:08:23 [Client Error] [{modId}]     cartographieavancee - Missing",
            $"13.8.2026 21:08:24 [Client Warning] [{modId}] a shape is missing, using a cube instead",
            $"13.8.2026 21:08:24 [Client Error] Patch 0 in {modId}:patches/cartes.json: File cartographieavancee:blocktypes/table.json not found");

        fileSystem.AddFile(launcher.GetLogFilePath(slug), new MockFileData(lines));
    }

    /// <summary>
    /// Installe un mod en retard et sert la réponse d'API qui annonce sa mise à jour : de quoi
    /// atteindre le dialogue de plan de mise à jour, un panneau que rien d'autre ne peuple.
    /// </summary>
    public static void SeedOutdatedMod(
        ServiceProvider provider,
        MockFileSystem fileSystem,
        FakeCatalogHandler catalogHandler,
        string slug)
    {
        var mods = provider.GetRequiredService<IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(slug),
            "configlib-1.0.0.zip",
            ModDbDoubles.ModInfo("configlib", "Config lib", "1.0.0"));

        catalogHandler.ModDb.UpdatesJson = """
        {
          "statuscode": "200",
          "updates": {
            "configlib": {
              "releaseid": 38314, "fileid": 84120, "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.11.1.zip",
              "filename": "configlib_1.11.1.zip", "downloads": 90210, "tags": ["1.21.3"], "modidstr": "configlib",
              "modversion": "1.11.1", "changelog": null, "created": "2026-02-11 09:22:10"
            }
          }
        }
        """;
    }
}