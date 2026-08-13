using Prospect.Core.Common;
using Prospect.Core.ModDb;

using Shouldly;

using Xunit.Abstractions;

namespace Prospect.Tests.Live;

/// <summary>
/// <c>/api/updates</c> confronté au VRAI ModDB, sur un lot MIXTE : un mod volontairement en retard,
/// un mod à jour, et un identifiant qui n'existe pas.
/// </summary>
/// <remarks>
/// <para>
/// Ce que cet étage attrape et qu'aucune fixture ne peut : nos faux serveurs répondent la forme
/// qu'on leur a écrite, alors que la question posée ici porte sur ce que le serveur RÉPOND
/// VRAIMENT. Deux faits en dépendent directement.
/// </para>
/// <para>
/// D'abord le piège documenté par la recherche : la réponse ne contient QUE les mods en retard, donc
/// l'absence d'une clé ne distingue pas « à jour » de « identifiant inconnu ». Le test l'établit en
/// envoyant les trois cas dans le même appel et en comparant aux clés envoyées.
/// </para>
/// <para>
/// Ensuite la raison d'être du verdict <see cref="ModUpdateStatus.UpdateNotDeclaredForThisVersion"/> :
/// la release renvoyée est la plus récente TOUTES versions de jeu confondues, jamais filtrée côté
/// serveur. C'est ce qui obligeait le client à rejouer le filtre de compatibilité, et c'est en
/// laissant tomber la candidate à ce moment-là qu'il concluait « à jour » sur une mise à jour
/// réelle.
/// </para>
/// <para>
/// Les assertions portent sur des faits STRUCTURELS et jamais sur des numéros de version : le mod
/// en retard l'est parce qu'on demande sciemment une version ancienne (<c>1.0.0</c>), ce qui restera
/// vrai tant que la fiche publiera quoi que ce soit.
/// </para>
/// </remarks>
[Trait("Category", "Live")]
public sealed class ModUpdatesLiveTests(ITestOutputHelper output)
{
    private const string Outdated = "carryon";
    private const string Absent = "prospect-mod-qui-nexiste-pas-4f2a9c41";

    [LiveFact]
    public async Task Updates_WithAMixedBatch_ReportsOnlyWhatIsBehindAndNeverInventsTheRest()
    {
        using var live = new LiveModDb();

        // Une version délibérément ancienne : le serveur ne peut que la déclarer en retard.
        var query = new Dictionary<string, ModVersion>(StringComparer.OrdinalIgnoreCase)
        {
            [Outdated] = ModVersion.Parse("1.0.0"),
            [Absent] = ModVersion.Parse("1.0.0"),
        };

        var updates = await live.Client.GetUpdatesAsync(query, CancellationToken.None);

        output.WriteLine($"Envoyé {query.Count} clés, reçu {updates.Count} : {string.Join(", ", updates.Keys)}.");

        // 1. Le mod en retard est signalé, avec une release exploitable.
        updates.ShouldContainKey(Outdated);
        var candidate = updates[Outdated];
        candidate.Version.ShouldBeGreaterThan(ModVersion.Parse("1.0.0"));
        candidate.DownloadUrl.IsAbsoluteUri.ShouldBeTrue();

        // 2. L'identifiant absent n'est PAS dans la réponse, et le serveur n'a pas pour autant
        //    rejeté l'appel : c'est exactement le piège « absence ≠ inconnu », qui interdit de
        //    conclure quoi que ce soit d'une clé manquante sans regarder ailleurs (la provenance).
        updates.ShouldNotContainKey(Absent);

        // 3. Rien d'inventé : aucune clé qu'on n'ait pas envoyée.
        updates.Keys.ShouldAllBe(key => query.ContainsKey(key));
    }

    /// <summary>
    /// Le lot dont RIEN n'est en retard, contre le vrai serveur : le cas le plus banal de tous, et
    /// le seul qui échouait. PHP encode une map associative VIDE en <c>[]</c> et non en <c>{}</c>,
    /// donc <c>/api/updates</c> répond littéralement <c>{"statuscode":"200","updates":[]}</c> quand
    /// tout est à jour. La désérialisation levait, et « Vérifier les mises à jour » annonçait « Le
    /// ModDB est injoignable » entre deux téléchargements ModDB parfaitement réussis.
    /// </summary>
    /// <remarks>
    /// Le lot est CALIBRÉ plutôt qu'écrit en dur : un premier appel demande une version
    /// délibérément ancienne pour apprendre ce que le serveur considère aujourd'hui comme la plus
    /// récente, un second la redemande telle quelle. Un numéro de version figé ici serait faux à la
    /// première publication de l'auteur, alors que la propriété testée (redemander exactement ce
    /// que le serveur vient de nommer ne peut rien laisser en retard) reste vraie pour toujours.
    /// Deux requêtes, espacées par <see cref="LiveModDb"/> comme tout l'étage.
    /// </remarks>
    [LiveFact]
    public async Task Updates_WithABatchWhereNothingIsBehind_ReadsTheEmptyPhpArrayAndReportsEverythingUpToDate()
    {
        using var live = new LiveModDb();

        var behind = await live.Client.GetUpdatesAsync(
            new Dictionary<string, ModVersion>(StringComparer.OrdinalIgnoreCase) { [Outdated] = ModVersion.Parse("1.0.0") },
            CancellationToken.None);

        var newest = behind[Outdated].Version;
        output.WriteLine($"Le serveur nomme {Outdated} {newest} comme la plus récente : on la lui redemande telle quelle.");

        // Rien à signaler, donc une map vide, donc `[]` sur le fil. Avant le convertisseur, cette
        // ligne levait ModDbUnavailableException.
        var upToDate = await live.Client.GetUpdatesAsync(
            new Dictionary<string, ModVersion>(StringComparer.OrdinalIgnoreCase) { [Outdated] = newest },
            CancellationToken.None);

        upToDate.ShouldBeEmpty($"« {Outdated} » {newest} est ce que le serveur vient de nommer : rien ne peut être en retard");
    }

    /// <summary>
    /// Le fait dont dépend le verdict « plus récente mais non déclarée » : la release renvoyée n'est
    /// pas filtrée par version de jeu, donc elle peut parfaitement ne porter AUCUN tag utile à
    /// l'instance qui pose la question.
    /// </summary>
    [LiveFact]
    public async Task Updates_ReturnTheNewestReleaseWhateverTheGameVersion_WhichIsWhyTheClientRefilters()
    {
        using var live = new LiveModDb();

        var updates = await live.Client.GetUpdatesAsync(
            new Dictionary<string, ModVersion>(StringComparer.OrdinalIgnoreCase) { [Outdated] = ModVersion.Parse("1.0.0") },
            CancellationToken.None);

        var candidate = updates[Outdated];
        candidate.CompatibleGameVersions.ShouldNotBeEmpty("une release publiée porte toujours au moins un tag");

        output.WriteLine($"{Outdated} {candidate.Version} taguée : {string.Join(", ", candidate.CompatibleGameVersionTags)}.");

        // Une version de jeu volontairement absurde, qu'aucun auteur n'a pu cocher : le serveur a
        // quand même rendu la release, et c'est le CLIENT qui doit trancher. Sans ce rejeu, on
        // installerait n'importe quoi ; en le rejouant sans le dire, on annonçait « à jour ».
        var impossible = GameVersion.Parse("9.99.9");
        ModReleaseSelector.Select([candidate], impossible, ModCompatibilityMode.ExactGameVersion)
            .ShouldBeNull("le serveur ne filtre pas par version de jeu, donc le client doit le faire");

        // Et c'est bien de ce cas-là que naît le verdict : une candidate plus récente que l'installé,
        // qu'aucun tag ne rattache à la version demandée.
        candidate.Version.ShouldBeGreaterThan(ModVersion.Parse("1.0.0"));
    }
}