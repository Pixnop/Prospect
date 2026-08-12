#if PROSPECT_CONFORMANCE_ENGINE
using Atlas.XUnit;

using Prospect.Core.ModDb;
using Prospect.GameConformance.Tests.Support;

// Prospect.Core.ModDb.ModInfo (le nôtre) et Vintagestory.API.Common.ModInfo (celui du moteur)
// partagent le même nom simple : cet alias lève l'ambiguïté sans renommer notre propre type.
using GameModInfo = Vintagestory.API.Common.ModInfo;

namespace Prospect.GameConformance.Tests;

/// <summary>
/// Fixtures <c>modinfo.json</c> tordues mais légales : chacune force une tolérance précise que
/// <see cref="ModInfoParser"/> documente (casse des clés, commentaires, virgules terminales, side
/// en minuscules, dépendances) sur un modid distinct, pour qu'un seul boot de serveur suffise à
/// tester les quatre en parallèle sans se marcher dessus.
/// </summary>
internal static class ModInfoParsingFixtures
{
    public const string MixedCaseKeysModId = "conformancemixedcase";
    public const string CommentsAndCommasModId = "conformancecommentscommas";
    public const string LowercaseSideModId = "conformancelowercaseside";
    public const string DependenciesModId = "conformancedependencies";

    // Casse des clés : ModID/Type/Name/Version/Side en tête de mot, comme le fait une partie non
    // négligeable des modinfo.json réels (voir la remarque de ModInfoParser sur modid/Modid/ModID).
    public const string MixedCaseKeysJson = """
        {
          "Type": "Content",
          "ModID": "conformancemixedcase",
          "Name": "Conformance Mixed Case",
          "Version": "0.4.2",
          "Side": "Universal"
        }
        """;

    // Commentaire de style JS et virgules terminales (tableau ET objet) : illégal en JSON strict,
    // courant dans les modinfo.json écrits à la main.
    public const string CommentsAndCommasJson = """
        {
          // Commentaire toléré par de nombreux mods réels.
          "type": "content",
          "modid": "conformancecommentscommas",
          "name": "Conformance Comments And Commas",
          "version": "2.3.1",
          "side": "universal",
          "authors": [
            "Prospect Conformance Suite",
          ],
        }
        """;

    // side en minuscules ("client" plutôt que "Client") : la doc de la classe du jeu écrit les
    // valeurs en PascalCase, la nature écrit souvent en minuscules.
    public const string LowercaseSideJson = """
        {
          "type": "content",
          "modid": "conformancelowercaseside",
          "name": "Conformance Lowercase Side",
          "version": "1.5.0",
          "side": "client"
        }
        """;

    // dependencies : uniquement les identifiants spéciaux (game/survival/creative), volontairement
    // — une VRAIE dépendance vers un mod absent désactiverait celui-ci côté moteur, ce qui
    // masquerait la comparaison au lieu de l'éclairer (ModInfoParser.IsSpecialDependencyId).
    public const string DependenciesJson = """
        {
          "type": "content",
          "modid": "conformancedependencies",
          "name": "Conformance Dependencies",
          "version": "1.2.0",
          "side": "universal",
          "dependencies": {
            "game": "1.20.0",
            "survival": "",
            "creative": "*"
          }
        }
        """;

    public static void WriteAll(string directory)
    {
        ModFixtureZip.AddFixture(directory, MixedCaseKeysModId + ".zip", MixedCaseKeysJson);
        ModFixtureZip.AddFixture(directory, CommentsAndCommasModId + ".zip", CommentsAndCommasJson);
        ModFixtureZip.AddFixture(directory, LowercaseSideModId + ".zip", LowercaseSideJson);
        ModFixtureZip.AddFixture(directory, DependenciesModId + ".zip", DependenciesJson);
    }
}

/// <summary>
/// PREUVE DE L'HYPOTHÈSE : <see cref="ModInfoParser"/> (parsing tolérant en <c>System.Text.Json</c>,
/// docs/architecture.md section « Client ModDB ») lit un <c>modinfo.json</c> tordu-mais-légal
/// EXACTEMENT comme le vrai <c>ModLoader</c> du moteur le lit — même identité, même version, même
/// side — pour chacune des tolérances que Prospect documente explicitement. Une seule installation
/// de serveur charge les quatre fixtures à la fois (modids distincts, aucune dépendance réelle
/// entre elles), quatre scénarios distincts les vérifient séparément pour qu'une régression sur
/// l'une n'en masque pas une autre. Divergence = échec précisant le champ fautif.
/// </summary>
[Trait("Category", "Conformance")]
[AtlasDataFiles("Fixtures/ModInfoParsing", TargetPath = "Mods")]
public sealed class ModInfoParsingAgreementTests : AtlasScenarioBase
{
    static ModInfoParsingAgreementTests()
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModInfoParsingAgreementTests).Assembly.Location)!,
            "Fixtures",
            "ModInfoParsing");

        // Nettoie tout résidu d'une exécution précédente avant de re-seeder les quatre fixtures :
        // WriteAll ajoute sans remplacer (AddFixture), donc un vieux fichier orphelin resterait sinon.
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        ModInfoParsingFixtures.WriteAll(directory);
    }

    [ConformanceFact]
    public async Task MixedCaseKeys_Should_ParseIdentically_When_KeysUseVaryingCase()
    {
        await World.Ticks(5);
        AssertAgreement(ModInfoParsingFixtures.MixedCaseKeysJson, ModInfoParsingFixtures.MixedCaseKeysModId);
    }

    [ConformanceFact]
    public async Task CommentsAndTrailingCommas_Should_ParseIdentically_When_JsonIsNotStrict()
    {
        await World.Ticks(5);
        AssertAgreement(ModInfoParsingFixtures.CommentsAndCommasJson, ModInfoParsingFixtures.CommentsAndCommasModId);
    }

    [ConformanceFact]
    public async Task LowercaseSide_Should_ParseIdentically_When_SideIsLowercase()
    {
        await World.Ticks(5);
        AssertAgreement(ModInfoParsingFixtures.LowercaseSideJson, ModInfoParsingFixtures.LowercaseSideModId);
    }

    [ConformanceFact]
    public async Task Dependencies_Should_ParseIdentically_When_OnlySpecialIdentifiersAreDeclared()
    {
        await World.Ticks(5);
        var (prospectInfo, engineInfo) = AssertAgreement(ModInfoParsingFixtures.DependenciesJson, ModInfoParsingFixtures.DependenciesModId);

        // VersionRequirement.ToString() rend toujours "*" pour « toute version », y compris quand
        // le fichier d'origine portait une chaîne vide (c'est un choix de rendu Prospect, déjà
        // couvert par Prospect.Core.Tests, pas une question de conformité au moteur) : on normalise
        // donc pareillement le côté moteur avant de comparer, pour ne comparer que ce qui relève
        // vraiment de l'accord entre les deux parseurs.
        var prospectDependencies = prospectInfo.Dependencies
            .ToDictionary(entry => entry.Key, entry => entry.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var engineDependencies = engineInfo.Dependencies
            .ToDictionary(
                dependency => dependency.ModID,
                dependency => string.IsNullOrEmpty(dependency.Version) ? "*" : dependency.Version,
                StringComparer.OrdinalIgnoreCase);

        Assert.True(
            prospectDependencies.Count == engineDependencies.Count,
            $"[{ModInfoParsingFixtures.DependenciesModId}] Champ dependencies : Prospect en lit " +
            $"{prospectDependencies.Count} ({string.Join(", ", prospectDependencies.Keys)}), le moteur en lit " +
            $"{engineDependencies.Count} ({string.Join(", ", engineDependencies.Keys)}).");

        foreach (var (modId, prospectVersion) in prospectDependencies)
        {
            Assert.True(
                engineDependencies.TryGetValue(modId, out var engineVersion),
                $"[{ModInfoParsingFixtures.DependenciesModId}] Champ dependencies : Prospect connaît la dépendance " +
                $"« {modId} », absente côté moteur.");
            Assert.True(
                string.Equals(prospectVersion, engineVersion, StringComparison.Ordinal),
                $"[{ModInfoParsingFixtures.DependenciesModId}] Champ dependencies[{modId}] : Prospect lit " +
                $"« {prospectVersion} », le moteur lit « {engineVersion} ».");
        }
    }

    /// <summary>Compare identité, version et side entre le parsing Prospect et le chargement réel
    /// du moteur pour une fixture, avec un message d'échec nommant le champ fautif.</summary>
    private (ModInfo Prospect, GameModInfo Engine) AssertAgreement(string rawJson, string expectedModId)
    {
        var prospectResult = ModInfoParser.Parse(rawJson);
        Assert.True(
            prospectResult.IsIdentified,
            $"[{expectedModId}] Le parseur Prospect n'a pas identifié ce modinfo.json pourtant légal : {prospectResult.Problem}.");
        var prospectInfo = prospectResult.Info!;

        var engineMod = World.Api.ModLoader.GetMod(expectedModId);
        Assert.True(
            engineMod is not null,
            $"[{expectedModId}] Le vrai ModLoader n'a pas chargé ce mod alors qu'il est légal (voir server-main.log " +
            "dans le scratch Atlas conservé pour ce test en échec).");
        var engineInfo = engineMod!.Info;

        Assert.True(
            string.Equals(prospectInfo.ModId, engineInfo.ModID, StringComparison.Ordinal),
            $"[{expectedModId}] Champ modid : Prospect lit « {prospectInfo.ModId} », le moteur lit « {engineInfo.ModID} ».");

        Assert.True(
            string.Equals(prospectInfo.RawVersion, engineInfo.Version, StringComparison.Ordinal),
            $"[{expectedModId}] Champ version : Prospect lit « {prospectInfo.RawVersion} », le moteur lit « {engineInfo.Version} ».");

        Assert.True(
            string.Equals(prospectInfo.Side.ToString(), engineInfo.Side.ToString(), StringComparison.Ordinal),
            $"[{expectedModId}] Champ side : Prospect lit « {prospectInfo.Side} », le moteur lit « {engineInfo.Side} ».");

        return (prospectInfo, engineInfo);
    }
}
#endif