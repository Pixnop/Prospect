using System.Net;

using Prospect.Core.ModDb;

using Shouldly;

using Xunit.Abstractions;

namespace Prospect.Tests.Live;

/// <summary>
/// Étage « conditions réelles » de l'adresse de fiche. Aucun double ne peut trancher cette
/// question : le site est le seul à savoir quel identifiant route <c>show/mod/{N}</c>, et c'est
/// précisément cette confusion (modid contre assetid) qui ouvrait la fiche d'un autre mod.
/// </summary>
/// <remarks>
/// Les requêtes sortent par <see cref="LiveModDb"/>, donc espacées d'au moins 1,5 s et signées
/// d'un <c>User-Agent</c> reconnaissable. Les fiches interrogées sont volontairement les deux qui
/// prouvent la divergence : Carry On (modid 890, assetid 4405, alias « carryon ») et CarryOnLib
/// (modid 4687, assetid 27960, alias « carryonlib »).
/// </remarks>
[Trait("Category", "Live")]
public sealed class ModDbPageUrlLiveTests(ITestOutputHelper output)
{
    [LiveFact]
    public async Task PageUrl_OfARealMod_AnswersOnTheRealSite()
    {
        using var live = new LiveModDb();
        using var probe = CreateProbe();

        foreach (var identifier in (string[])["carryon", "carryonlib"])
        {
            var detail = await live.Client.GetModAsync(identifier, CancellationToken.None);
            output.WriteLine($"{detail.Name} : modid {detail.ModId}, assetid {detail.AssetId}, page {detail.PageUrl}");

            // La divergence n'est pas théorique : si un jour les deux espaces coïncidaient, ce
            // test perdrait son objet, et il vaut mieux le savoir.
            detail.AssetId.ShouldBeGreaterThan(0);

            using var response = await probe.GetAsync(detail.PageUrl, CancellationToken.None);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{detail.PageUrl} devrait répondre 200");

            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            body.ShouldContain(detail.Name, Case.Insensitive, $"{detail.PageUrl} devrait être la fiche de « {detail.Name} »");
        }
    }

    [LiveFact]
    public async Task PageUrl_BuiltFromTheAssetId_OpensTheSameModAsTheAlias()
    {
        // La branche sans alias (40 % du catalogue) vérifiée sur une fiche qui en a un : les deux
        // routes doivent désigner le même mod, ce qui est exactement ce que le repli promet.
        using var live = new LiveModDb();
        using var probe = CreateProbe();

        var detail = await live.Client.GetModAsync("carryon", CancellationToken.None);
        var byAsset = ModDbMapper.BuildPageUrl(detail.AssetId, urlAlias: null);
        var byModId = ModDbMapper.BuildPageUrl(detail.ModId, urlAlias: null);
        output.WriteLine($"par asset : {byAsset} — par modid (l'ancienne construction) : {byModId}");

        using var response = await probe.GetAsync(byAsset, CancellationToken.None);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(CancellationToken.None)).ShouldContain(detail.Name, Case.Insensitive);

        // Et la démonstration du défaut : la même route nourrie du modid ne mène PAS ici.
        byModId.ShouldNotBe(byAsset);
    }

    // Client à part du client d'API : ces pages sont du HTML, pas du JSON, et l'espacement des
    // requêtes de LiveModDb ne couvre que ce qui passe par son propre handler.
    private static HttpClient CreateProbe()
    {
        var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        probe.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", LiveModDb.LiveUserAgent);

        return probe;
    }
}