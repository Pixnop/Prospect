using System.Text.Json.Serialization;

namespace Prospect.Core.Common;

/// <summary>
/// Canal de diffusion d'une version du jeu, dérivé de son suffixe. Aligné sur
/// <c>deriveType()</c> du VS Launcher (docs/research/vslauncher-et-distribution.md, section b) :
/// <c>-rc</c> puis <c>-pre</c>, tout le reste est stable. <see cref="Dev"/> étend ce même
/// principe au suffixe <c>-dev</c> que gère notre grammaire (absent du catalogue observé au
/// moment de la recherche, mais prévu par la regex du ModDB).
/// </summary>
public enum GameVersionChannel
{
    Stable,
    Rc,
    Pre,
    Dev,
}

/// <summary>
/// Version d'une installation du jeu Vintage Story (catalogue <c>stable.json</c>/
/// <c>unstable.json</c>, champ <c>gameVersion</c> d'<c>instance.json</c>...). Value object
/// immuable : deux <see cref="GameVersion"/> aux mêmes composantes sont interchangeables, il
/// n'y a pas d'identité au-delà de la valeur.
/// </summary>
/// <remarks>
/// Type public volontairement distinct de <see cref="ModVersion"/> : bien que les deux
/// partagent la même grammaire et le même ordre (réutilisés via le cœur interne
/// <see cref="SemanticVersionCore"/>), rien dans l'API ne permet de comparer une version de jeu
/// à une version de mod, deux notions qui n'ont pas de sens l'une par rapport à l'autre.
/// </remarks>
[JsonConverter(typeof(GameVersionJsonConverter))]
public readonly record struct GameVersion : IComparable<GameVersion>, IEquatable<GameVersion>
{
    private readonly SemanticVersionCore _core;

    private GameVersion(SemanticVersionCore core) => _core = core;

    /// <summary>
    /// Cœur interne, exposé uniquement à l'assemblage courant : c'est ce qui permet à
    /// <see cref="VersionRequirement"/> de comparer une borne minimale à une version de jeu
    /// sans introduire de comparaison publique entre <see cref="GameVersion"/> et
    /// <see cref="ModVersion"/>.
    /// </summary>
    internal SemanticVersionCore Core => _core;

    /// <summary>Composante majeure.</summary>
    public int Major => _core.Major;

    /// <summary>Composante mineure.</summary>
    public int Minor => _core.Minor;

    /// <summary>Composante de patch.</summary>
    public int Patch => _core.Patch;

    /// <summary>
    /// Canal déduit du suffixe de version. <see cref="GameVersionChannel.Stable"/> pour une
    /// version finale, sans suffixe.
    /// </summary>
    public GameVersionChannel Channel => _core.Tag switch
    {
        PreReleaseTag.Dev => GameVersionChannel.Dev,
        PreReleaseTag.Pre => GameVersionChannel.Pre,
        PreReleaseTag.Rc => GameVersionChannel.Rc,
        _ => GameVersionChannel.Stable,
    };

    /// <summary>
    /// Parse strict, aligné sur la regex <c>compileSemanticVersion()</c> du ModDB :
    /// <c>Major.Minor.Patch</c> avec suffixe optionnel <c>-dev.N</c>, <c>-pre.N</c> ou
    /// <c>-rc.N</c>. Aucun préfixe, aucun espace toléré ici (voir <see cref="TryParse"/> pour
    /// une variante tolérante).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> est <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="value"/> ne respecte pas la grammaire.</exception>
    public static GameVersion Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!SemanticVersionCore.TryParseStrict(value, out var core))
        {
            throw new FormatException($"'{value}' n'est pas une version de jeu valide (attendu : Major.Minor.Patch[-dev|pre|rc.N]).");
        }

        return new GameVersion(core);
    }

    /// <summary>
    /// Parse tolérant : accepte en plus un préfixe <c>v</c>/<c>V</c> et des espaces de bordure,
    /// pour absorber les versions de jeu glanées dans des sources externes moins disciplinées
    /// que le catalogue officiel. Ne relâche rien d'autre : la casse des étiquettes de suffixe
    /// reste stricte, aucun wildcard n'est reconnu.
    /// </summary>
    public static bool TryParse(string? value, out GameVersion result)
    {
        if (SemanticVersionCore.TryParseLenient(value, out var core))
        {
            result = new GameVersion(core);
            return true;
        }

        result = default;
        return false;
    }

    /// <inheritdoc />
    public int CompareTo(GameVersion other) => _core.CompareTo(other._core);

    /// <inheritdoc />
    public bool Equals(GameVersion other) => _core.Equals(other._core);

    /// <inheritdoc />
    public override int GetHashCode() => _core.GetHashCode();

    /// <summary>Round-trip vers la forme canonique, par exemple <c>1.22.0-rc.10</c>.</summary>
    public override string ToString() => _core.ToString();

    public static bool operator <(GameVersion left, GameVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(GameVersion left, GameVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(GameVersion left, GameVersion right) => left.CompareTo(right) > 0;

    public static bool operator >=(GameVersion left, GameVersion right) => left.CompareTo(right) >= 0;
}