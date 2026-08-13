using Prospect.Core.Common;
using Prospect.Core.ModDb;

using Shouldly;

using Xunit.Abstractions;

namespace Prospect.Tests.Live;

/// <summary>
/// Le cas précis remonté par la session de test Windows, confronté au VRAI ModDB : installer
/// « Carry On » 2.0.0-pre.8 sur une instance en 1.22.6 annonçait « Introuvable sur le ModDB :
/// carryonlib ». C'est faux, et seul un appel réel peut le prouver — nos fixtures ne peuvent
/// qu'affirmer la topologie qu'on leur a écrite. Ce test-ci va chercher les deux fiches et vérifie,
/// sur les tags que le serveur publie aujourd'hui, que le verdict à rendre est « aucune version
/// compatible » et jamais « introuvable ».
/// </summary>
/// <remarks>
/// Opt-in comme tout l'étage live (<c>PROSPECT_LIVE=1</c>, requêtes espacées d'au moins 1,5 s par
/// <see cref="LiveModDb"/>). Les assertions portent sur des FAITS STRUCTURELS, jamais sur des
/// numéros de version précis : que la fiche de <c>carryonlib</c> existe, et que la relation entre
/// les deux jeux de tags soit celle qu'on croit. Le jour où l'auteur publiera une release taguée
/// pour la version de jeu la plus haute de <c>carryon</c>, l'écart disparaîtra sans que ce test
/// devienne rouge : il rapporte alors ce qu'il voit et se contente de la partie qui reste vraie
/// (la fiche existe).
/// </remarks>
[Trait("Category", "Live")]
public sealed class ModDependencyVerdictLiveTests(ITestOutputHelper output)
{
    [LiveFact]
    public async Task CarryOnLib_IsPublishedOnTheModDb_EvenWhenNoReleaseCoversTheInstanceGameVersion()
    {
        using var live = new LiveModDb();

        // 1. La fiche de la dépendance EXISTE. C'est le fait que le message « introuvable »
        //    contredisait. Si elle n'existait pas, GetModAsync lèverait ModDbApiException.
        var library = await live.Client.GetModAsync("carryonlib", CancellationToken.None);

        output.WriteLine($"carryonlib : fiche {library.ModId} « {library.Name} », {library.Releases.Count} releases.");
        library.ModId.ShouldBeGreaterThan(0);
        library.Name.ShouldNotBeNullOrWhiteSpace();
        library.Releases.ShouldNotBeEmpty();

        // 2. Le mod demandeur, dont une release au moins cible une version de jeu plus haute que
        //    tout ce que la bibliothèque couvre : c'est cet ÉCART qui produit le cas.
        var mod = await live.Client.GetModAsync("carryon", CancellationToken.None);

        var libraryVersions = library.Releases.SelectMany(release => release.CompatibleGameVersions).ToArray();
        var modVersions = mod.Releases.SelectMany(release => release.CompatibleGameVersions).ToArray();
        libraryVersions.ShouldNotBeEmpty();
        modVersions.ShouldNotBeEmpty();

        var highestForMod = modVersions.Max();
        var highestForLibrary = libraryVersions.Max();
        output.WriteLine($"Version de jeu la plus haute — carryon : {highestForMod}, carryonlib : {highestForLibrary}.");

        // 3. Le verdict, calculé par la MÊME brique que la production (ModReleaseSelector) sur les
        //    données réelles, pour la version de jeu la plus récente que carryon revendique — c'est
        //    la situation de l'instance qui a produit le rapport. La comparaison se limite à cette
        //    version : les vieilles releases de carryon (1.14.3, taguée jusqu'à 1.17) sont
        //    antérieures à l'existence même de la bibliothèque, l'écart y est normal et sans rapport.
        var choice = ModReleaseSelector.Select(library.Releases, highestForMod, ModCompatibilityMode.ExactGameVersion);

        if (choice is not null)
        {
            // L'auteur a rattrapé ses tags depuis le relevé : le cas ne se produit plus, et c'est
            // une bonne nouvelle, pas une régression. La partie qui compte reste prouvée (la fiche
            // existe), et le rapport le dit.
            output.WriteLine($"L'écart a disparu : carryonlib est désormais taguée {highestForMod} (release {choice.Release.Version}).");

            return;
        }

        output.WriteLine($"Aucune release de carryonlib n'est taguée {highestForMod} : verdict « aucune version compatible ».");

        // Et c'est bien un problème de TAGS, pas d'existence : la même recherche élargie à la série
        // mineure retrouve une release de cette même fiche. « Introuvable » n'a jamais été le mot.
        var widened = ModReleaseSelector
            .Select(library.Releases, highestForMod, ModCompatibilityMode.WidenToMinorSeries)
            .ShouldNotBeNull($"la fiche carryonlib publie bien des releases sur la série de {highestForMod}");

        output.WriteLine($"Élargie à la série, la même fiche rend {widened.Release.Version} : la fiche est bien vivante.");
        ModReleaseSelector.IsSameMinorSeries(highestForLibrary, highestForMod).ShouldBeTrue();
    }

    /// <summary>
    /// Le contre-exemple, sur la même infrastructure : un identifiant qui n'existe vraiment pas
    /// doit, lui, faire lever le client. C'est ce qui autorise le verdict « introuvable » à exister
    /// encore, et empêche de le remplacer partout par le nouveau texte.
    /// </summary>
    [LiveFact]
    public async Task AnIdentifierThatIsReallyAbsent_StillMakesTheClientThrow()
    {
        using var live = new LiveModDb();

        await Should.ThrowAsync<ModDbApiException>(
            () => live.Client.GetModAsync("prospect-mod-qui-nexiste-pas-4f2a9c41", CancellationToken.None));
    }
}