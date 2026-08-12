#if !PROSPECT_CONFORMANCE_ENGINE
namespace Prospect.GameConformance.Tests;

/// <summary>
/// Sentinelle compilée UNIQUEMENT quand <c>VINTAGE_STORY</c> n'était pas défini au moment du
/// build (voir le <c>PropertyGroup</c> conditionnel du <c>.csproj</c>) : dans ce cas, les vrais
/// scénarios de conformité (<c>DisabledConventionTests</c>, <c>DataPathLayoutTests</c>,
/// <c>ModInfoParsingAgreementTests</c>) ne sont même pas compilés, faute de pouvoir résoudre les
/// types du jeu réel (<c>BlockPos</c>, <c>ICoreServerAPI</c>...). Ce test unique les remplace pour
/// que « dotnet test » de toute la solution continue de rapporter un résultat clair — un test
/// ignoré, jamais une erreur de compilation — plutôt que de faire disparaître silencieusement ce
/// projet du rapport de tests.
/// </summary>
public sealed class EngineUnavailableTests
{
    [Fact(Skip =
        "VINTAGE_STORY n'était pas défini à la compilation : les scénarios de conformité réels " +
        "n'ont pas pu être compilés (ils ont besoin des types du jeu). Ce test sentinelle les " +
        "remplace pour que la solution reste verte. Voir tests/Prospect.GameConformance.Tests/README.md.")]
    public void ConformanceSuite_Should_CompileRealScenarios_When_VintageStoryIsInstalled()
    {
        // Jamais exécuté (toujours Skip) : le corps n'a qu'à exister pour que la méthode compile.
    }
}
#endif