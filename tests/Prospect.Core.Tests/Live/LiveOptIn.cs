namespace Prospect.Tests.Live;

/// <summary>
/// L'unique opt-in de l'étage « conditions réelles » : tant que <see cref="EnvironmentVariableName"/>
/// ne vaut pas <c>"1"</c>, les tests qui en dépendent sont rapportés « Skipped » sans qu'aucune
/// socket ne s'ouvre. Même mécanique et même raison d'être que <c>ConformanceFactAttribute</c>
/// (tests/Prospect.GameConformance.Tests) : un test qui sort de la machine ne doit jamais
/// s'exécuter par accident.
/// </summary>
/// <remarks>
/// <para>
/// Ce que ces tests attrapent, aucun double ne le peut : le catalogue réel fait 8 000 fiches là où
/// nos fakes en ont trois, le vocabulaire de <c>/api/tags</c> en compte plus de deux cents là où
/// nos fakes en ont trois, et une fiche réelle porte des champs et des volumes que personne n'a
/// pensé à écrire dans un fake (voir docs/research/moddb-api.md).
/// </para>
/// <para>
/// La contrepartie est qu'ils appellent un service tiers gratuit : ils restent donc hors de la
/// gate de pull request (ci.yml n'est pas touché), ne s'exécutent que sur déclenchement manuel ou
/// hebdomadaire (.github/workflows/conformance.yml), espacent leurs requêtes
/// (<see cref="LiveModDb"/>) et se nomment dans leur <c>User-Agent</c>.
/// </para>
/// <para>
/// Le motif de saut est calculé UNE fois, à l'initialisation du type, et lu par le constructeur de
/// chaque attribut live : XUnit lit <c>Skip</c> par son nom au moment de la découverte, donc un
/// motif non nul suffit à ce qu'aucun corps de test ne soit jamais invoqué.
/// </para>
/// </remarks>
public static class LiveOptIn
{
    /// <summary>Variable d'environnement qui déclenche l'exécution réelle des tests live quand elle vaut « 1 ».</summary>
    public const string EnvironmentVariableName = "PROSPECT_LIVE";

    /// <summary>Motif de saut à poser sur <c>Skip</c>, ou <see langword="null"/> quand l'opt-in est actif.</summary>
    public static string? SkipReason { get; } =
        Environment.GetEnvironmentVariable(EnvironmentVariableName) == "1"
            ? null
            : "Ignoré par défaut : ce test interroge les VRAIS services Vintage Story "
                + "(mods.vintagestory.at, api et cdn.vintagestory.at). "
                + $"Pour l'exécuter : {EnvironmentVariableName}=1 dotnet test.";
}