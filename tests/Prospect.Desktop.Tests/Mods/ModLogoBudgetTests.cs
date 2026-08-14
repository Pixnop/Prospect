using System.IO.Abstractions.TestingHelpers;

using Avalonia.Headless.XUnit;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.ModDb;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.Support;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;

using Shouldly;

namespace Prospect.Desktop.Tests.Mods;

/// <summary>
/// Ce que les vignettes ajoutées hors du navigateur coûtent au budget d'octets de la strate
/// VIGNETTE (<see cref="ModLogoCache.MaxCachedThumbnailBytes"/>, 24 Mio), toutes surfaces ouvertes
/// ensemble. La question n'est pas rhétorique : ce budget avait été taillé quand une seule surface
/// affichait des logos, et l'élargir en silence serait exactement le défaut que la PR 48 a corrigé.
/// </summary>
/// <remarks>
/// <para>
/// Le pire cas est monté à la main, plus dur que le réel sur les deux axes qui comptent. Les
/// vignettes servies sont CARRÉES à 480 points, donc réduites en 128×128 (64 Kio de pixels
/// chacune), là où le CDN sert plutôt du 480×320 qui devient 128×85 (42,5 Kio). Et l'instance porte
/// quatre-vingts mods venus du ModDB, tous à logo, tous ABSENTS de la fenêtre du navigateur : ils ne
/// peuvent donc réutiliser aucune entrée déjà payée.
/// </para>
/// <para>
/// C'est ce dernier point qui décide de tout, et il vaut d'être écrit : la clé du cache est
/// <c>largeur|url</c>, et TOUTES les surfaces de ce chantier décodent à la largeur de la carte du
/// navigateur. Un mod vu dans le navigateur puis retrouvé dans l'onglet Mods, dans un plan
/// d'installation et dans une confirmation de retrait occupe UNE entrée, pas quatre. Le coût réel
/// de ce chantier n'est donc pas la somme de ses surfaces, mais le nombre de mods DISTINCTS qu'une
/// session finit par regarder.
/// </para>
/// <para>
/// Mesuré par ce test : le navigateur seul, à son plafond de 150 cartes, occupe 6,25 Mio pour
/// 100 entrées (une carte sur trois du générateur n'a pas de logo). Les quatre-vingts rangées de
/// l'onglet, le docteur et la confirmation de retrait par-dessus portent le total à 11,25 Mio pour
/// 180 entrées, soit 47 % du budget de vignettes et 35 % du plafond d'entrées. L'onglet Mods coûte
/// donc 5 Mio dans ce pire cas, et à peine plus de 3 Mio avec des vignettes de la forme réelle du
/// CDN. Aucun élargissement de budget n'est nécessaire, et c'est ce qui devait être vérifié plutôt
/// que supposé.
/// </para>
/// </remarks>
public sealed class ModLogoBudgetTests
{
    private const int CatalogSize = 8_000;

    /// <summary>
    /// Mods installés dans l'instance de mesure. Au-delà de ce qu'un joueur ordinaire pose, et
    /// délibérément : c'est le nombre qui décide du coût des rangées.
    /// </summary>
    private const int InstalledModCount = 80;

    /// <summary>
    /// Premier identifiant de fiche installé. Choisi loin devant la fenêtre de rendu du navigateur
    /// pour qu'aucune de ces vignettes ne réutilise une entrée déjà en cache : sans cet écart, la
    /// mesure serait flatteuse.
    /// </summary>
    private const int FirstInstalledModId = 2_000;

    [AvaloniaFact]
    public async Task ToutesLesSurfacesOuvertesEnsemble_TiennentDansLeBudgetDeVignettes()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem, out var handler);
        handler.ModDb.CatalogJson = LargeCatalog.CatalogJson(CatalogSize);
        handler.ModDb.LogoBytes = TinyPng.Create(480);

        // Tout le catalogue est déclaré compatible : le navigateur ciblé sur une instance ne
        // montre que les mods compatibles, et une grille réduite à deux cartes rendrait la mesure
        // complaisante là où on la veut au pire cas.
        handler.ModDb.CompatibleModIds = [.. Enumerable.Range(1, CatalogSize)];

        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var slug = await provider.SeedTargetInstanceAsync(ResponsiveScenario.LongInstanceName);
        SeedManyModDbMods(provider, fileSystem, slug);

        // 1. Le navigateur, poussé jusqu'à son plafond de rendu : la surface qui consommait déjà ce
        //    budget avant ce chantier. Sans instance cible, pour que la grille voie le catalogue
        //    ENTIER : ciblée, elle se restreint aux mods déclarés compatibles, ce qui rendrait la
        //    mesure complaisante.
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        for (var slice = 0; slice < 40; slice++)
        {
            shell.ModBrowser.ShowMoreCommand.Execute(null);
        }

        foreach (var card in shell.ModBrowser.Results)
        {
            await card.LogoLoadCompletion;
        }

        window.Settle();
        var afterBrowser = Cache(provider).CachedThumbnailBytes;

        // 2. L'onglet Mods de l'instance, quatre-vingts rangées, aucune n'ayant croisé le navigateur.
        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        detail.SelectTabCommand.Execute(InstanceDetailTab.Mods);
        foreach (var row in detail.ModsTab.Mods)
        {
            await row.Thumbnail.LoadCompletion;
        }

        window.Settle();
        detail.ModsTab.Mods.Count.ShouldBe(InstalledModCount);
        detail.ModsTab.Mods.Count(row => row.Thumbnail.HasLogo).ShouldBe(InstalledModCount);

        // 3. Le docteur par-dessus, puis la confirmation de retrait : les deux surfaces modales que
        //    l'onglet peut ouvrir sans quitter la page.
        await detail.CheckInstanceCommand.ExecuteAsync(null);
        shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();
        shell.Overlay.Close();

        await detail.ModsTab.Mods[0].RemoveCommand.ExecuteAsync(null);
        var uninstall = shell.Overlay.Active.ShouldBeOfType<UninstallModDialogViewModel>();
        await uninstall.Thumbnail.LoadCompletion;
        window.Settle();

        var cache = Cache(provider);

        // Le budget tient, et il tient LARGEMENT : c'est le fait à rapporter, pas une victoire au
        // point près. Le plafond d'entrées reste l'autre garde-fou, indépendant de la forme des
        // images.
        cache.CachedThumbnailBytes.ShouldBeLessThanOrEqualTo(
            ModLogoCache.MaxCachedThumbnailBytes,
            $"vignettes mémorisées : {cache.CachedThumbnailBytes / 1024 / 1024} Mio pour {cache.CachedCount} entrées");
        cache.CachedCount.ShouldBeLessThanOrEqualTo(ModLogoCache.MaxCachedBitmaps);

        // Et l'onglet a bien COÛTÉ quelque chose : une mesure qui ne bouge pas ne mesurerait rien.
        cache.CachedThumbnailBytes.ShouldBeGreaterThan(afterBrowser);

        window.Close();
    }

    /// <summary>
    /// La même fiche vue par deux surfaces n'occupe qu'UNE entrée : toutes décodent à la largeur de
    /// la carte du navigateur, et la clé du cache est <c>largeur|url</c>. C'est l'invariant qui
    /// empêche ce chantier de multiplier le budget par le nombre d'écrans.
    /// </summary>
    [AvaloniaFact]
    public async Task UnModVuDansLeNavigateurPuisDansLOnglet_NOccupeQuUneEntree()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem, out _);
        var window = ResponsiveScenario.ShowWindow(provider);
        var shell = provider.GetRequiredService<ShellViewModel>();
        var slug = await provider.SeedTargetInstanceAsync(ResponsiveScenario.LongInstanceName);
        ResponsiveScenario.SeedModDbMod(provider, fileSystem, slug, "betterruins", "BetterRuins", modDbModId: 792);

        shell.ShowModBrowser(slug);
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        foreach (var card in shell.ModBrowser.Results)
        {
            await card.LogoLoadCompletion;
        }

        window.Settle();
        var afterBrowser = Cache(provider).CachedThumbnailBytes;
        afterBrowser.ShouldBeGreaterThan(0);

        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        detail.SelectTabCommand.Execute(InstanceDetailTab.Mods);
        var row = detail.ModsTab.Mods.ShouldHaveSingleItem();
        await row.Thumbnail.LoadCompletion;
        window.Settle();

        row.Thumbnail.HasLogo.ShouldBeTrue();
        Cache(provider).CachedThumbnailBytes.ShouldBe(afterBrowser, "la rangée réutilise l'entrée déjà payée par la carte");

        window.Close();
    }

    private static ModLogoCache Cache(ServiceProvider provider)
        => provider.GetRequiredService<IModLogoCache>().ShouldBeOfType<ModLogoCache>();

    // Des mods dont le modid textuel et l'identifiant de fiche suivent la convention du générateur
    // de catalogue : chacun a donc une URL de logo distincte, donc une entrée de cache distincte.
    private static void SeedManyModDbMods(ServiceProvider provider, MockFileSystem fileSystem, string slug)
    {
        var mods = provider.GetRequiredService<IInstalledModRepository>();
        var entries = new List<string>(InstalledModCount);

        for (var offset = 0; offset < InstalledModCount; offset++)
        {
            // Le générateur ne pose un logo que sur deux fiches sur trois : on ne retient que
            // celles qui en ont un, pour que la mesure porte bien sur quatre-vingts vignettes.
            var modDbModId = FirstInstalledModId + (offset * 3) + 1;
            var modIdString = $"testmod{modDbModId}";
            var fileName = $"{modIdString}-1.0.0.zip";

            ModDbDoubles.SeedMod(
                fileSystem,
                mods.GetModsDirectory(slug),
                fileName,
                ModDbDoubles.ModInfo(modIdString, $"Mod de test {modDbModId}"));

            entries.Add($$"""
                { "fileName": "{{fileName}}", "modId": {{modDbModId}}, "modIdString": "{{modIdString}}",
                  "releaseId": 1, "fileId": 1, "version": "1.0.0", "installedUtc": "2026-08-01T09:00:00+00:00" }
                """);
        }

        fileSystem.AddFile(
            mods.GetProvenanceFilePath(slug),
            new MockFileData($$"""
            { "schemaVersion": 1, "mods": [ {{string.Join(',', entries)}} ] }
            """));
    }
}