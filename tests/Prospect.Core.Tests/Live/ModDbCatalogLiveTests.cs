using Prospect.Core.ModDb;

using Shouldly;

using Xunit.Abstractions;

namespace Prospect.Tests.Live;

/// <summary>
/// Étage « conditions réelles » du client ModDB : le catalogue entier et le vocabulaire de tags,
/// tels que le serveur les rend aujourd'hui. Ces deux chiffres dimensionnent deux corrections de
/// cette PR (le rendu paresseux de la grille, et la rangée de tags contenue) : les figer ici, en
/// les RAPPORTANT plutôt qu'en les figeant à une valeur exacte, évite qu'un test devienne rouge
/// parce qu'un auteur a publié un mod de plus.
/// </summary>
[Trait("Category", "Live")]
public sealed class ModDbCatalogLiveTests(ITestOutputHelper output)
{
    /// <summary>
    /// (a) Le catalogue complet, mappé jusqu'à la dernière entrée. Aucune exception tolérée : le
    /// mapper doit absorber les ~8 000 fiches réelles, champs nuls et tableaux à chaîne vide
    /// compris (docs/research/moddb-api.md).
    /// </summary>
    [LiveFact]
    public async Task Catalog_MapsEveryRealEntry_WithoutThrowing()
    {
        using var live = new LiveModDb();

        var catalog = await live.Client.GetCatalogAsync(forceRefresh: true, CancellationToken.None);

        output.WriteLine($"Catalogue réel : {catalog.Mods.Count} mods mappés, fraîcheur {catalog.Freshness}.");
        catalog.Freshness.ShouldBe(ModDbFreshness.Live);

        // Le catalogue de production dépassait 8 000 fiches au moment de cette PR. Le plancher est
        // volontairement bas : ce test doit détecter une réponse tronquée ou un mapping qui jette
        // tout, pas signaler la publication ou le retrait d'un mod.
        catalog.Mods.Count.ShouldBeGreaterThan(5_000);

        // Chaque entrée doit être exploitable par la grille : un nom, une page, et des collections
        // jamais nulles (la carte les lit sans les tester).
        foreach (var mod in catalog.Mods)
        {
            mod.ModId.ShouldBeGreaterThan(0);
            mod.Name.ShouldNotBeNullOrWhiteSpace();
            mod.Tags.ShouldNotBeNull();
            mod.ModIdStrings.ShouldNotBeNull();
            mod.Tags.ShouldAllBe(tag => tag.Length > 0);
        }

        var withoutLogo = catalog.Mods.Count(mod => mod.LogoUrl is null);
        var withoutModIdString = catalog.Mods.Count(mod => mod.ModIdStrings.Count == 0);
        var multiModIdString = catalog.Mods.Count(mod => mod.ModIdStrings.Count > 1);
        var withoutTag = catalog.Mods.Count(mod => mod.Tags.Count == 0);
        output.WriteLine(
            $"Sans logo : {withoutLogo}. Sans modidstr : {withoutModIdString}. "
            + $"Multi-modidstr : {multiModIdString}. Sans tag : {withoutTag}.");
    }

    /// <summary>
    /// (b) Le vocabulaire réel de <c>/api/tags</c>. C'est ce compte qui condamne la rangée de tags
    /// telle qu'elle était dessinée : avec les trois tags des doubles, un <c>WrapPanel</c> tenait
    /// sur une ligne ; avec le vrai vocabulaire, il occupe la moitié de la page.
    /// </summary>
    [LiveFact]
    public async Task Tags_ReportsTheRealVocabularySize()
    {
        using var live = new LiveModDb();

        var tags = await live.Client.GetTagsAsync(forceRefresh: true, CancellationToken.None);

        output.WriteLine($"Vocabulaire réel de /api/tags : {tags.Count} tags.");
        output.WriteLine("Premiers : " + string.Join(", ", tags.Take(10).Select(tag => tag.Name)));

        // Le vocabulaire dépassait 200 entrées au moment de cette PR : très au-delà de ce qu'une
        // rangée peut montrer, ce qui est exactement le problème à corriger.
        tags.Count.ShouldBeGreaterThan(50);
        tags.ShouldAllBe(tag => tag.Name.Length > 0 && tag.TagId.Length > 0);
    }
}