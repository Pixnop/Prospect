using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Instances;
using Prospect.Core.ModDb;
using Prospect.Desktop.Layout;
using Prospect.Desktop.Tests.Support;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Wizard;
using Prospect.Desktop.Views.Mods;

using Shouldly;

namespace Prospect.Desktop.Tests.Mods;

/// <summary>
/// Écrans de mods montés dans le shell réel, avec le conteneur de production sur système de
/// fichiers et réseau factices (docs/architecture.md, exigence de test headless).
/// </summary>
public sealed class ModsHeadlessTests
{
    private static async Task<InstanceRecord> CreateInstanceAsync(ShellViewModel shell, HomeViewModel home, string name, string version)
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

    [AvaloniaFact]
    public async Task ModBrowser_LoadsAndRendersItsResults()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        shell.CurrentPage.ShouldBeOfType<ModBrowserViewModel>();
        window.GetVisualDescendants().OfType<ModBrowserView>().ShouldNotBeEmpty();
        shell.ModBrowser.Results.ShouldNotBeEmpty();
    }

    [AvaloniaFact]
    public async Task ModBrowser_CardsRenderLogosAndSummaries_MatchingTheDesign()
    {
        using var provider = TestServiceProviderFactory.Create(out _, out var catalogHandler);

        // Un troisième mod sans logo ni résumé, en plus des deux du catalogue par défaut (Config
        // lib : résumé sans logo ; BetterRuins : résumé et logo), pour vérifier que la carte reste
        // propre dans ce cas (design/ui_kits/launcher/screen-mods.jsx : la ligne de description et
        // l'image sont ABSENTES, pas un blanc réservé).
        catalogHandler.ModDb.CatalogJson = """
        {
          "statuscode": "200",
          "mods": [
            { "modid": 1783, "assetid": 9551, "downloads": 627953, "follows": 0, "trendingpoints": 5, "comments": 0,
              "name": "Config lib", "summary": "A universal place to configure your mods.", "modidstrs": ["configlib"],
              "author": "Maltiez", "urlalias": null, "side": "both", "type": "mod", "logo": null,
              "tags": ["Utility"], "lastreleased": "2026-05-01 12:03:34" },
            { "modid": 792, "assetid": 3829, "downloads": 1095320, "follows": 8133, "trendingpoints": 0, "comments": 1440,
              "name": "BetterRuins", "summary": "Adds many new ruins to your survival world.", "modidstrs": ["betterruins"],
              "author": "NiclAss", "urlalias": "betterruins", "side": "both", "type": "mod",
              "logo": "https://moddbcdn.vintagestory.at/betterruins.png",
              "tags": ["Worldgen", "Exploration"], "lastreleased": "2026-07-28 18:59:32" },
            { "modid": 555, "assetid": 1, "downloads": 3, "follows": 0, "trendingpoints": 0, "comments": 0,
              "name": "Blank Mod", "summary": "", "modidstrs": ["blankmod"],
              "author": "Personne", "urlalias": null, "side": "both", "type": "mod", "logo": null,
              "tags": [], "lastreleased": null }
          ]
        }
        """;

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        shell.ModBrowser.Results.Count.ShouldBe(3);
        foreach (var card in shell.ModBrowser.Results)
        {
            await card.LogoLoadCompletion;
        }

        window.Settle();

        var withLogo = shell.ModBrowser.Results.Single(card => card.Name == "BetterRuins");
        withLogo.HasLogo.ShouldBeTrue();
        withLogo.HasDescription.ShouldBeTrue();

        var withoutLogo = shell.ModBrowser.Results.Single(card => card.Name == "Config lib");
        withoutLogo.HasLogo.ShouldBeFalse();
        withoutLogo.HasDescription.ShouldBeTrue();

        var blank = shell.ModBrowser.Results.Single(card => card.Name == "Blank Mod");
        blank.HasLogo.ShouldBeFalse();
        blank.HasDescription.ShouldBeFalse();

        // Rendu réel, pas seulement l'état du ViewModel : chaque carte a un Image dans son arbre
        // visuel (toujours présent, voir ModBrowserView.axaml), avec une Source seulement pour
        // celle dont le logo a été décodé.
        var images = window.GetVisualDescendants().OfType<Image>().ToList();
        images.Single(image => ReferenceEquals(image.DataContext, withLogo)).Source.ShouldNotBeNull();
        images.Single(image => ReferenceEquals(image.DataContext, withoutLogo)).Source.ShouldBeNull();
        images.Single(image => ReferenceEquals(image.DataContext, blank)).Source.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task ModBrowser_ModDbUnreachable_RendersTheOfflineBanner()
    {
        using var provider = TestServiceProviderFactory.Create(out _, out var handler);
        handler.ModDb.IsOnline = false;
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        shell.ModBrowser.IsOffline.ShouldBeTrue();
        shell.ModBrowser.ShowEmptyState.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task ModDetailDialog_OpensOverTheBrowser()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        await shell.ModBrowser.Results.Single(card => card.Name == "Config lib").OpenCommand.ExecuteAsync(null);
        window.Settle();

        shell.Overlay.Active.ShouldBeOfType<ModDetailDialogViewModel>();
        window.GetVisualDescendants().OfType<ModDetailDialogView>().ShouldNotBeEmpty();
    }

    /// <summary>
    /// Le chemin exact du plantage rapporté en test réel : ouvrir une fiche, la refermer par son
    /// bouton, puis laisser l'interface se remettre en page. Le shell réel est utilisé, donc le VRAI
    /// <c>OverlayService</c>, qui dispose ce qu'il ferme.
    /// </summary>
    [AvaloniaFact]
    public async Task ModDetailDialog_ClosedFromItsButton_LeavesTheBrowserAlive()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        await shell.ModBrowser.Results.Single(card => card.Name == "Config lib").OpenCommand.ExecuteAsync(null);
        window.Settle();
        var dialog = shell.Overlay.Active.ShouldBeOfType<ModDetailDialogViewModel>();

        Should.NotThrow(() => dialog.CloseCommand.Execute(null));

        // Plusieurs passes : le défaut d'origine se manifestait à la mise en page qui suit la
        // fermeture, pas au clic lui-même.
        window.Settle();
        window.Settle();

        shell.Overlay.Active.ShouldBeNull();
        window.GetVisualDescendants().OfType<ModDetailDialogView>().ShouldBeEmpty();
        window.GetVisualDescendants().OfType<ModBrowserView>().ShouldNotBeEmpty();
    }

    /// <summary>
    /// La même fermeture, mais par REMPLACEMENT de panneau et pendant qu'un chargement d'image est
    /// encore en vol : l'autre moitié du contrat de possession de l'overlay.
    /// </summary>
    [AvaloniaFact]
    public async Task ModDetailDialog_ReplacedWhileImagesAreStillLoading_DoesNotThrow()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        await shell.ModBrowser.Results.Single(card => card.Name == "Config lib").OpenCommand.ExecuteAsync(null);
        var dialog = shell.Overlay.Active.ShouldBeOfType<ModDetailDialogViewModel>();

        // Aucun Settle avant : la fiche est remplacée alors que ses images sont encore en vol.
        Should.NotThrow(() => shell.Overlay.Show(new StopInstanceDialogViewModel("Homestead", () => { }, shell.Overlay)));
        window.Settle();

        Should.NotThrow(dialog.Dispose);
        shell.Overlay.Active.ShouldBeOfType<StopInstanceDialogViewModel>();
    }

    [AvaloniaFact]
    public async Task InstanceModsTab_RendersTheRealListInsteadOfAPlaceholder()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        var record = await CreateInstanceAsync(shell, home, "Homestead", "1.21.3");

        var mods = provider.GetRequiredService<IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(record.Slug),
            "configlib-1.11.1.zip",
            ModDbDoubles.ModInfo("configlib", "Config lib", "1.11.1"));

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        detail.SelectedTab.ShouldBe(InstanceDetailTab.Mods);
        detail.ModsTab.Mods.ShouldHaveSingleItem().Name.ShouldBe("Config lib");
        window.GetVisualDescendants().OfType<InstanceModsTabView>().ShouldNotBeEmpty();
    }

    [AvaloniaFact]
    public async Task InstanceModsTab_CheckUpdatesThenUpdateOneMod_BadgesTheRowAndAppliesTheReplacement()
    {
        // Bout en bout sur des fakes (feature 4b) : vérification, badge, dialog de plan,
        // confirmation, disparition du badge une fois le fichier remplacé.
        using var provider = TestServiceProviderFactory.Create(out var fileSystem, out var catalogHandler);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        var record = await CreateInstanceAsync(shell, home, "Homestead", "1.21.3");

        var mods = provider.GetRequiredService<IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(record.Slug),
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

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        await detail.ModsTab.CheckUpdatesCommand.ExecuteAsync(null);
        window.Settle();

        var row = detail.ModsTab.Mods.ShouldHaveSingleItem();
        row.HasUpdateAvailable.ShouldBeTrue();
        detail.ModsTab.AvailableUpdateCount.ShouldBe(1);
        window.GetVisualDescendants().OfType<InstanceModsTabView>().ShouldNotBeEmpty();

        await row.UpdateCommand.ExecuteAsync(null);
        window.Settle();

        shell.Overlay.Active.ShouldBeOfType<ModUpdatePlanDialogViewModel>();
        window.GetVisualDescendants().OfType<ModUpdatePlanDialogView>().ShouldNotBeEmpty();

        var dialog = (ModUpdatePlanDialogViewModel)shell.Overlay.Active!;
        await dialog.ConfirmCommand.ExecuteAsync(null);
        window.Settle();

        detail.ModsTab.Mods.ShouldHaveSingleItem().HasUpdateAvailable.ShouldBeFalse();
        shell.Overlay.Active.ShouldBeNull();
    }

    /// <summary>
    /// Les pastilles du dernier lancement sont RENDUES, et leur libellé est celui qu'on lit dans
    /// la fenêtre : un ViewModel qui compte juste ne prouve pas qu'un badge s'affiche.
    /// </summary>
    [AvaloniaFact]
    public async Task InstanceModsTab_AfterALaunchThatWentBadly_RendersTheBadgesOfTheGuiltyMod()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        var record = await CreateInstanceAsync(shell, home, "Homestead", "1.21.3");

        var mods = provider.GetRequiredService<IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(record.Slug),
            "configlib-1.0.0.zip",
            ModDbDoubles.ModInfo("configlib", "Config lib", "1.0.0"));
        ResponsiveScenario.SeedLaunchLogBlaming(provider, fileSystem, record.Slug, "configlib");

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        var row = detail.ModsTab.Mods.ShouldHaveSingleItem();
        row.LogErrorsText.ShouldBe("3 erreurs au dernier lancement");
        row.IntegrationText.ShouldBe("attend du contenu de cartographieavancee");

        var rendered = window.GetVisualDescendants().OfType<ContentControl>()
            .Where(control => control.Classes.Contains("badge") && control.IsVisible)
            .Select(control => control.Content as string)
            .ToArray();

        rendered.ShouldContain(row.LogErrorsText);
        rendered.ShouldContain(row.LogWarningsText);
        rendered.ShouldContain(row.IntegrationText);

        window.Close();
    }

    [AvaloniaFact]
    public async Task HomeCard_ReflectsAnUpdateCheckDoneFromTheInstanceTab_WithoutReloadingTheGrid()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem, out var catalogHandler);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        var record = await CreateInstanceAsync(shell, home, "Homestead", "1.21.3");
        var card = home.Instances.ShouldHaveSingleItem();
        card.HasUpdates.ShouldBeFalse();

        var mods = provider.GetRequiredService<IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(record.Slug),
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

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        await detail.ModsTab.CheckUpdatesCommand.ExecuteAsync(null);

        // ShowHome() ne recharge pas la grille (voir ShellViewModel) : la même InstanceCardViewModel
        // doit refléter le changement en direct via IModUpdateCheckCache.Changed, pas via un rescan.
        shell.ShowHome();
        window.Settle();

        card.HasUpdates.ShouldBeTrue();
        card.UpdateCount.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task BrowseFromTheInstanceTab_OpensTheBrowserPrefilteredOnThatInstance()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        var record = await CreateInstanceAsync(shell, home, "Homestead", "1.21.3");

        shell.ShowInstanceDetail(record.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);

        detail.ModsTab.BrowseCommand.Execute(null);
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        shell.CurrentPage.ShouldBeOfType<ModBrowserViewModel>();
        shell.ModBrowser.SelectedInstance!.Slug.ShouldBe(record.Slug);
    }

    /// <summary>
    /// Aucune pastille d'une carte n'est jamais À MOITIÉ visible : chacune est entièrement dans le
    /// champ de sa rangée, ou entièrement dehors. Rien entre les deux, à aucune des trois tailles
    /// de la garde et dans les deux thèmes.
    /// </summary>
    /// <remarks>
    /// La rangée défilait, barre masquée, et la garde précédente ne surveillait que « Installé ·
    /// version ». Le résultat n'était donc pas une pastille cachée mais une pastille COUPÉE EN
    /// PLEIN MOT contre le bouton « Gérer » (« cli », « client et ») : trois pastilles demandent
    /// environ 330 points quand une carte de la grille fluide n'en offre que 200 à 300. Un glyphe
    /// tranché se lit comme un défaut d'affichage, pas comme un choix, d'où la mesure sur CHAQUE
    /// pastille plutôt que sur la première.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task ModBrowser_NoBadgeOfACard_IsEverPartlyVisible(string variant)
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        var window = ResponsiveScenario.ShowWindow(provider, variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var slug = await provider.SeedTargetInstanceAsync(ResponsiveScenario.LongInstanceName);
        ResponsiveScenario.SeedInstalledMod(provider, fileSystem, slug);

        shell.ShowModBrowser(slug);
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        var installedText = shell.ModBrowser.Results.Single(card => card.IsInstalled).InstalledText;

        foreach (var size in ResponsiveWindowSizes.All)
        {
            window.Width = size.Width;
            window.Height = size.Height;
            window.Settle();

            var rows = window.GetVisualDescendants().OfType<PriorityRowPanel>().ToArray();
            rows.ShouldNotBeEmpty($"aucune rangée de pastilles rendue à {size}");

            foreach (var row in rows)
            {
                foreach (var badge in row.Children.OfType<ContentControl>().Where(control => control.IsVisible))
                {
                    var left = badge.Bounds.X;
                    var right = badge.Bounds.Right;
                    var label = badge.Content as string ?? string.Empty;

                    // Entièrement dedans, ou entièrement dehors. Le milieu est le défaut.
                    var inside = left >= -0.5d && right <= row.Bounds.Width + 0.5d;
                    var outside = left >= row.Bounds.Width - 0.5d;

                    (inside || outside).ShouldBeTrue(
                        $"la pastille « {label} » est coupée à {size} : {left:0.#}..{right:0.#} dans une rangée de {row.Bounds.Width:0.#}");
                }
            }

            // Et la priorité tient : « Installé · version » est celle qu'on garde toujours.
            var installed = rows
                .SelectMany(row => row.Children.OfType<ContentControl>().Select(badge => (Row: row, Badge: badge)))
                .Single(pair => Equals(pair.Badge.Content, installedText) && pair.Badge.IsVisible);

            installed.Badge.Bounds.Width.ShouldBeGreaterThan(0d, $"la pastille « {installedText} » n'est pas mesurée à {size}");
            installed.Badge.Bounds.Right.ShouldBeLessThanOrEqualTo(
                installed.Row.Bounds.Width + 0.5d,
                $"la pastille « {installedText} » sort du champ de sa rangée à {size}");
        }

        window.Close();
    }

    /// <summary>
    /// La rangée écarte des pastilles ENTIÈRES et le dit : l'indicateur d'écart paraît exactement
    /// quand quelque chose a été écarté, jamais autrement.
    /// </summary>
    [AvaloniaFact]
    public async Task ModBrowser_ACardTooNarrowForEveryBadge_DropsWholeBadgesAndShowsTheOverflowMark()
    {
        using var provider = ResponsiveScenario.CreateProvider(out var fileSystem, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var slug = await provider.SeedTargetInstanceAsync(ResponsiveScenario.LongInstanceName);
        ResponsiveScenario.SeedInstalledMod(provider, fileSystem, slug);

        shell.ShowModBrowser(slug);
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Width = ResponsiveWindowSizes.Floor.Width;
        window.Height = ResponsiveWindowSizes.Floor.Height;
        window.Settle();

        var installedText = shell.ModBrowser.Results.Single(card => card.IsInstalled).InstalledText;
        var row = window
            .GetVisualDescendants()
            .OfType<PriorityRowPanel>()
            .Single(candidate => candidate.Children.OfType<ContentControl>().Any(badge => Equals(badge.Content, installedText)));

        row.DroppedCount.ShouldBeGreaterThan(0, "trois pastilles ne tiennent pas dans une carte de la grille au plancher");

        var marker = row.Children
            .OfType<ContentControl>()
            .Single(PriorityRowPanel.GetIsOverflowIndicator);

        marker.Bounds.Right.ShouldBeLessThanOrEqualTo(row.Bounds.Width + 0.5d, "l'indicateur d'écart déborde de sa rangée");
        marker.Bounds.Width.ShouldBeGreaterThan(0d);

        window.Close();
    }
}