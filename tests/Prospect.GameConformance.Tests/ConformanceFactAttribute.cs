using Xunit.Sdk;

namespace Prospect.GameConformance.Tests;

/// <summary>
/// Variante d'<c>Atlas.XUnit.AtlasScenarioAttribute</c> qui saute le scénario tant que la variable
/// d'environnement <see cref="EnvironmentVariableName"/> ne vaut pas <c>"1"</c>. C'est l'unique
/// mécanisme d'opt-in de tout ce projet : sans elle, un scénario démarre un vrai serveur Vintage
/// Story headless dès qu'on exécute <c>dotnet test</c>, y compris dans la CI normale.
/// </summary>
/// <remarks>
/// <para>
/// <c>AtlasScenarioAttribute</c> est scellée (<c>sealed</c>) : impossible d'en hériter pour
/// surcharger <c>Skip</c> dynamiquement. Cet attribut la MIROITE donc entièrement — même nom de
/// propriétés, même découvreur XUnit référencé par réflexion (<c>Atlas.XUnit.Internal.
/// AtlasScenarioDiscoverer</c>, résolu par son nom de type et d'assembly, pas par référence de
/// compilation) — plutôt que d'en hériter. Ça fonctionne parce
/// qu'<c>AtlasScenarioDiscoverer.Discover</c> lit ces propriétés par leur NOM via
/// <c>IAttributeInfo.GetNamedArgument&lt;T&gt;(nameof(...))</c>, jamais par le type déclarant
/// réel de l'attribut : un miroir aux mêmes noms produit exactement le même comportement
/// (vérifié en lisant le code source d'Atlas avant d'écrire cette classe, pas supposé).
/// </para>
/// <para>
/// Le <c>Skip</c> hérité de <c>FactAttribute</c> est lui posé au moment de la construction, donc
/// avant toute découverte XUnit : quand il est non vide, XUnit rapporte le test comme « Skipped »
/// sans jamais appeler <c>AtlasTestInvoker</c>, donc sans jamais tenter de localiser
/// <c>VINTAGE_STORY</c> ni de démarrer de serveur. C'est ce qui rend le saut instantané.
/// </para>
/// <para>
/// N'a elle-même AUCUNE dépendance de compilation vers Atlas ou Vintage Story (le découvreur est
/// désigné par une chaîne, pas par un <c>using</c>) : ce fichier compile identiquement que
/// <c>PROSPECT_CONFORMANCE_ENGINE</c> soit défini ou non. Seuls les scénarios qui l'utilisent
/// vraiment (donc qui héritent d'<c>AtlasScenarioBase</c>) sont, eux, cantonnés derrière ce
/// symbole — voir le README du projet.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
[XunitTestCaseDiscoverer("Atlas.XUnit.Internal.AtlasScenarioDiscoverer", "Atlas.XUnit")]
public sealed class ConformanceFactAttribute : FactAttribute
{
    /// <summary>Variable d'environnement qui déclenche l'exécution réelle de ce test quand elle vaut « 1 ».</summary>
    public const string EnvironmentVariableName = "PROSPECT_CONFORMANCE";

    /// <summary>Construit l'attribut, en lisant l'opt-in immédiatement.</summary>
    public ConformanceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariableName) != "1")
        {
            Skip = $"Ignoré par défaut : ce test démarre un vrai serveur Vintage Story headless et " +
                   $"confronte Prospect au moteur réel (voir tests/Prospect.GameConformance.Tests/README.md). " +
                   $"Pour l'exécuter : {EnvironmentVariableName}=1 dotnet test tests/Prospect.GameConformance.Tests.";
        }
    }

    /// <summary>Cf. <c>AtlasScenarioAttribute.FreshWorld</c> : reflétée ici par son nom, jamais utilisée par nos scénarios.</summary>
    public bool FreshWorld { get; set; }

    /// <summary>Cf. <c>AtlasScenarioAttribute.RollbackWorld</c>.</summary>
    public bool RollbackWorld { get; set; }

    /// <summary>Cf. <c>AtlasScenarioAttribute.RestartWorld</c>.</summary>
    public bool RestartWorld { get; set; }

    /// <summary>Cf. <c>AtlasScenarioAttribute.StrictIsolation</c>.</summary>
    public bool StrictIsolation { get; set; }

    /// <summary>Cf. <c>AtlasScenarioAttribute.TimeoutMs</c> ; même valeur par défaut (60 s).</summary>
    public int TimeoutMs { get; set; } = 60_000;
}