using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.ModDb;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Tests.Live;

using Shouldly;

using Xunit.Abstractions;

namespace Prospect.Desktop.Tests.Live;

/// <summary>
/// Le chemin du clic joué sur un ÉCHANTILLON de formes du catalogue réel, choisi par ce que les
/// fiches ont d'inhabituel plutôt qu'au hasard : ce sont ces formes-là qu'aucun double n'écrit
/// spontanément, et c'est sur elles que la fiche plantait.
/// </summary>
/// <remarks>
/// L'échantillon est dérivé du catalogue du jour, donc il change tout seul quand le dépôt change :
/// c'est voulu. Un jeu d'identifiants figé finirait par ne plus couvrir que des fiches devenues
/// banales, alors que le but est justement d'aller chercher ce que le dépôt a d'anormal
/// aujourd'hui. La taille reste bornée pour que le nombre de requêtes le soit aussi.
/// </remarks>
[Trait("Category", "Live")]
public sealed class ModCatalogShapesLiveTests(ITestOutputHelper output)
{
    /// <summary>Nombre de fiches par forme inhabituelle, et donc borne du nombre de requêtes.</summary>
    private const int PerShape = 4;

    [LiveAvaloniaFact]
    public async Task EveryUnusualShapeOfTheRealCatalog_SurvivesTheClickPath()
    {
        using var live = new LiveModDb();
        var catalog = await live.Client.GetCatalogAsync(forceRefresh: true, CancellationToken.None);
        output.WriteLine($"Catalogue réel : {catalog.Mods.Count} mods.");

        using var provider = TestServiceProviderFactory.Create(out _);
        var shell = provider.GetRequiredService<ShellViewModel>();

        var failures = new List<string>();
        foreach (var (shape, mod) in PickShapes(catalog.Mods))
        {
            try
            {
                var detail = await live.Client.GetModAsync(
                    mod.ModId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    CancellationToken.None);

                var dialog = new ModDetailDialogViewModel(
                    detail,
                    shell.ModBrowser.SelectedInstance,
                    new FakeExternalUrlOpener(),
                    shell.Overlay,
                    ModDbDoubles.CreateLogoCache(),
                    () => Task.CompletedTask);

                output.WriteLine(
                    $"  [{shape}] {mod.ModId} « {dialog.Name} » : {detail.Releases.Count} releases, "
                    + $"description {dialog.Description.Document.Blocks.Count} blocs.");
            }
            catch (Exception exception)
            {
                failures.Add($"[{shape}] {mod.ModId} « {mod.Name} » : {exception.GetType().Name} — {exception.Message}");
            }
        }

        failures.ShouldBeEmpty(
            "Ouvrir la fiche d'un mod réel ne doit jamais lever :" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    // Les formes que docs/research/moddb-api.md signale comme réellement présentes dans le dépôt,
    // plus les extrêmes de volume : ce sont les seuls axes sur lesquels une fiche peut surprendre
    // sans qu'on l'ait écrit soi-même dans un fake.
    private static IEnumerable<(string Shape, ModDbModSummary Mod)> PickShapes(IReadOnlyList<ModDbModSummary> mods)
    {
        var seen = new HashSet<int>();

        IEnumerable<(string, ModDbModSummary)> Take(string shape, IEnumerable<ModDbModSummary> candidates)
            => candidates.Where(mod => seen.Add(mod.ModId)).Take(PerShape).Select(mod => (shape, mod));

        return
        [
            .. Take("le plus téléchargé", mods.OrderByDescending(mod => mod.Downloads)),
            .. Take("sans logo", mods.Where(mod => mod.LogoUrl is null).OrderByDescending(mod => mod.Downloads)),
            .. Take("sans modidstr", mods.Where(mod => mod.ModIdStrings.Count == 0).OrderByDescending(mod => mod.Downloads)),
            .. Take("plusieurs modidstr", mods.Where(mod => mod.ModIdStrings.Count > 1).OrderByDescending(mod => mod.Downloads)),
            .. Take("sans tag", mods.Where(mod => mod.Tags.Count == 0).OrderByDescending(mod => mod.Downloads)),
            .. Take("outil externe", mods.Where(mod => mod.Type is "externaltool" or "other").OrderByDescending(mod => mod.Downloads)),
            .. Take("jamais publié", mods.Where(mod => mod.LastReleasedUtc is null)),
            .. Take("le plus récent", mods.OrderByDescending(mod => mod.LastReleasedUtc ?? DateTimeOffset.MinValue)),
            .. Take("le plus ancien", mods.Where(mod => mod.LastReleasedUtc is not null).OrderBy(mod => mod.LastReleasedUtc)),
        ];
    }
}