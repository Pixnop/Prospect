namespace Prospect.Core.Tests.Common;

/// <summary>
/// Échantillons de chaînes de version, réels ou construits à partir de la grammaire
/// documentée dans docs/research/moddb-api.md (regex <c>compileSemanticVersion</c> du ModDB).
/// Partagés entre <see cref="GameVersionTests"/> et <see cref="ModVersionTests"/> puisque les
/// deux types appliquent exactement la même grammaire et le même ordre : dupliquer les
/// méthodes de test est voulu (chaque type public mérite sa propre couverture complète et
/// lisible seule), dupliquer les chaînes elles-mêmes ne l'est pas.
/// </summary>
internal static class VersionSamples
{
    /// <summary>
    /// Chaînes valides au sens strict, avec leurs composantes attendues
    /// (Major, Minor, Patch, étiquette de suffixe ou null si version finale, numéro de suffixe).
    /// </summary>
    public static TheoryData<string, int, int, int, string?, int> Valid => new()
    {
        { "1.22.6", 1, 22, 6, null, 0 },
        { "1.21.3", 1, 21, 3, null, 0 },
        { "1.12.0", 1, 12, 0, null, 0 },
        { "1.22.0-pre.5", 1, 22, 0, "pre", 5 },
        { "1.22.0-rc.10", 1, 22, 0, "rc", 10 },
        { "1.22.0-rc.2", 1, 22, 0, "rc", 2 },
        { "1.22.0-dev.1", 1, 22, 0, "dev", 1 },
        { "0.0.1", 0, 0, 1, null, 0 },
        { "10.20.30", 10, 20, 30, null, 0 },
    };

    /// <summary>
    /// Chaînes invalides, rejetées aussi bien par le parsing strict que tolérant : aucune de
    /// ces formes n'existe dans la grammaire du ModDB (ni wildcard, ni composante manquante ou
    /// surnuméraire, ni étiquette de suffixe inconnue).
    /// </summary>
    public static TheoryData<string> Invalid => new()
    {
        "",
        "1.2",
        "1.2.3.4",
        "1.20.*",
        "abc",
        "1.22.0-beta.1",
        "1.2.3-rc",
        "1.2.3-rc.",
        "1.2.3-rc.x",
        "1.a.3",
        "1..3",
        ".1.2.3",
        "1.2.3.",
        "1.2.3-RC.1",
        "-1.2.3",
        "1.2.3-",
        "1.2.3-pre.1.2",
        "99999999999999999999.0.0",
        "1.2.3-rc.99999999999999999999",
    };

    /// <summary>
    /// Chaînes rejetées par le parsing strict mais acceptées par le parsing tolérant, avec la
    /// forme canonique attendue une fois reformatée par <c>ToString()</c>. Les versions de mods
    /// glanées dans la nature ont un préfixe <c>v</c>/<c>V</c> ou des espaces de bordure ; rien
    /// d'autre n'est toléré (la casse des étiquettes de suffixe reste stricte).
    /// </summary>
    public static TheoryData<string, string> LenientOnly => new()
    {
        { "v1.22.6", "1.22.6" },
        { "V1.22.6", "1.22.6" },
        { " 1.22.6", "1.22.6" },
        { "1.22.6 ", "1.22.6" },
        { "  v1.22.0-rc.10  ", "1.22.0-rc.10" },
        { "v1.22.0-dev.1", "1.22.0-dev.1" },
    };

    /// <summary>
    /// Paires (version basse, version haute) utilisées pour vérifier l'ordre. Comprend le
    /// piège classique du tri lexicographique sur le numéro de suffixe (<c>rc.2 &lt; rc.10</c>,
    /// qu'un tri par chaîne inverserait).
    /// </summary>
    public static TheoryData<string, string> OrderedPairs => new()
    {
        { "1.21.3", "1.22.6" },
        { "1.12.0", "1.21.3" },
        { "1.9.14", "1.22.6" },
        { "1.22.0-dev.1", "1.22.0-pre.1" },
        { "1.22.0-pre.1", "1.22.0-rc.1" },
        { "1.22.0-pre.5", "1.22.0-rc.2" },
        { "1.22.0-rc.10", "1.22.0" },
        { "1.22.0-rc.2", "1.22.0-rc.10" },
        { "1.22.0-rc.10", "1.22.0-rc.100" },
        { "1.22.0-dev.1", "1.22.0" },
        { "1.22.0-dev.9", "1.22.0-dev.10" },
    };
}