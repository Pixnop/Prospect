using System.Text.Json.Serialization;

namespace Prospect.Core.Common;

/// <summary>
/// Version d'un mod du ModDB Vintage Story (champ <c>version</c> de <c>modinfo.json</c>,
/// <c>modversion</c> de l'API). Value object immuable : deux <see cref="ModVersion"/> aux
/// mêmes composantes sont interchangeables, il n'y a pas d'identité au-delà de la valeur.
/// </summary>
/// <remarks>
/// Type public volontairement distinct de <see cref="GameVersion"/> : bien que les deux
/// partagent la même grammaire et le même ordre (réutilisés via le cœur interne
/// <see cref="SemanticVersionCore"/>), rien dans l'API ne permet de comparer une version de mod
/// à une version de jeu, deux notions qui n'ont pas de sens l'une par rapport à l'autre.
/// </remarks>
[JsonConverter(typeof(ModVersionJsonConverter))]
public readonly record struct ModVersion : IComparable<ModVersion>, IEquatable<ModVersion>
{
    private readonly SemanticVersionCore _core;

    private ModVersion(SemanticVersionCore core) => _core = core;

    /// <summary>
    /// Cœur interne, exposé uniquement à l'assemblage courant : c'est ce qui permet à
    /// <see cref="VersionRequirement"/> de comparer une borne minimale à une version de mod
    /// sans introduire de comparaison publique entre <see cref="ModVersion"/> et
    /// <see cref="GameVersion"/>.
    /// </summary>
    internal SemanticVersionCore Core => _core;

    /// <summary>Composante majeure.</summary>
    public int Major => _core.Major;

    /// <summary>Composante mineure.</summary>
    public int Minor => _core.Minor;

    /// <summary>Composante de patch.</summary>
    public int Patch => _core.Patch;

    /// <summary>
    /// Parse strict, aligné sur la regex <c>compileSemanticVersion()</c> du ModDB :
    /// <c>Major.Minor.Patch</c> avec suffixe optionnel <c>-dev.N</c>, <c>-pre.N</c> ou
    /// <c>-rc.N</c>. Aucun préfixe, aucun espace toléré ici (voir <see cref="TryParse"/> pour
    /// une variante tolérante).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> est <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="value"/> ne respecte pas la grammaire.</exception>
    public static ModVersion Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!SemanticVersionCore.TryParseStrict(value, out var core))
        {
            throw new FormatException($"'{value}' n'est pas une version de mod valide (attendu : Major.Minor.Patch[-dev|pre|rc.N]).");
        }

        return new ModVersion(core);
    }

    /// <summary>
    /// Parse tolérant : accepte en plus un préfixe <c>v</c>/<c>V</c> et des espaces de bordure,
    /// pour absorber les versions de mods glanées dans la nature (auteurs multiples, formatage
    /// jamais homogène, cf. docs/research/moddb-api.md). Ne relâche rien d'autre : la casse des
    /// étiquettes de suffixe reste stricte, aucun wildcard n'est reconnu.
    /// </summary>
    public static bool TryParse(string? value, out ModVersion result)
    {
        if (SemanticVersionCore.TryParseLenient(value, out var core))
        {
            result = new ModVersion(core);
            return true;
        }

        result = default;
        return false;
    }

    /// <inheritdoc />
    public int CompareTo(ModVersion other) => _core.CompareTo(other._core);

    /// <inheritdoc />
    public bool Equals(ModVersion other) => _core.Equals(other._core);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <summary>Round-trip vers la forme canonique, par exemple <c>1.8.0-rc.10</c>.</summary>
    public override string ToString() => _core.ToString();

    public static bool operator <(ModVersion left, ModVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(ModVersion left, ModVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(ModVersion left, ModVersion right) => left.CompareTo(right) > 0;

    public static bool operator >=(ModVersion left, ModVersion right) => left.CompareTo(right) >= 0;
}