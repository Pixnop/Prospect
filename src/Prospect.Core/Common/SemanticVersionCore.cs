using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Prospect.Core.Common;

/// <summary>
/// Étiquette de pré-release d'une version, dans son ordre de maturité croissant. La valeur
/// numérique porte l'ordre : c'est ce qui permet à <see cref="SemanticVersionCore.CompareTo"/>
/// de comparer les étiquettes par simple comparaison entière, <see cref="None"/> (version
/// finale) valant toujours plus que n'importe quelle pré-release.
/// </summary>
internal enum PreReleaseTag
{
    Dev = 0,
    Pre = 1,
    Rc = 2,
    None = 3,
}

/// <summary>
/// Cœur de parsing et de comparaison partagé par <see cref="GameVersion"/> et
/// <see cref="ModVersion"/>. Les deux types publics restent volontairement incomparables entre
/// eux (l'API ne doit pas permettre de comparer une version de jeu à une version de mod), mais
/// ils suivent exactement la même grammaire : c'est ce type interne, jamais exposé, qui porte
/// l'unique implémentation du parsing et de l'ordre.
/// </summary>
/// <remarks>
/// Grammaire, alignée sur la regex du ModDB (<c>lib/version.php</c>,
/// <c>compileSemanticVersion()</c>, documentée dans docs/research/moddb-api.md) :
/// <c>Major.Minor.Patch</c> avec suffixe optionnel <c>-dev.N</c>, <c>-pre.N</c> ou <c>-rc.N</c>.
/// <see cref="TryParseStrict"/> applique cette grammaire telle quelle. <see cref="TryParseLenient"/>
/// tolère en plus un préfixe <c>v</c>/<c>V</c> et des espaces en début/fin de chaîne (les
/// versions de mods glanées dans la nature sont sales), mais ne relâche rien d'autre : la casse
/// des étiquettes de suffixe reste stricte (<c>RC</c> reste invalide), et aucun format de
/// wildcard n'est reconnu.
/// </remarks>
internal readonly partial record struct SemanticVersionCore(int Major, int Minor, int Patch, PreReleaseTag Tag, int TagNumber)
    : IComparable<SemanticVersionCore>
{
    // Grammaire stricte : `Major.Minor.Patch` puis, en option, `-(dev|pre|rc).N`. Aucun préfixe,
    // aucun espace toléré.
    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)(?:-(dev|pre|rc)\.(\d+))?$")]
    private static partial Regex StrictPattern();

    // Même grammaire, avec un préfixe `v`/`V` optionnel et des espaces de bordure tolérés.
    [GeneratedRegex(@"^\s*[vV]?(\d+)\.(\d+)\.(\d+)(?:-(dev|pre|rc)\.(\d+))?\s*$")]
    private static partial Regex LenientPattern();

    public static bool TryParseStrict(string? value, out SemanticVersionCore result)
        => TryParseCore(value, StrictPattern(), out result);

    public static bool TryParseLenient(string? value, out SemanticVersionCore result)
        => TryParseCore(value, LenientPattern(), out result);

    private static bool TryParseCore(string? value, Regex pattern, out SemanticVersionCore result)
    {
        result = default;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var match = pattern.Match(value);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor)
            || !int.TryParse(match.Groups[3].Value, out var patch))
        {
            return false;
        }

        var tagGroup = match.Groups[4];
        if (!tagGroup.Success)
        {
            result = new SemanticVersionCore(major, minor, patch, PreReleaseTag.None, 0);
            return true;
        }

        if (!int.TryParse(match.Groups[5].Value, out var tagNumber))
        {
            return false;
        }

        // La regex ci-dessus n'autorise que "dev"/"pre"/"rc" dans ce groupe : les autres cas
        // sont donc impossibles à atteindre, pas une simple erreur d'entrée à valider.
        var tag = tagGroup.Value switch
        {
            "dev" => PreReleaseTag.Dev,
            "pre" => PreReleaseTag.Pre,
            "rc" => PreReleaseTag.Rc,
            _ => throw new UnreachableException($"Suffixe inattendu capturé par la regex : '{tagGroup.Value}'."),
        };

        result = new SemanticVersionCore(major, minor, patch, tag, tagNumber);
        return true;
    }

    public int CompareTo(SemanticVersionCore other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        result = Tag.CompareTo(other.Tag);
        if (result != 0)
        {
            return result;
        }

        // Le numéro de suffixe se compare numériquement, jamais comme une chaîne : "rc.2" doit
        // être inférieur à "rc.10", alors qu'un tri lexicographique naïf conclurait l'inverse.
        return TagNumber.CompareTo(other.TagNumber);
    }

    // Opérateurs exigés de pair avec IComparable<T> (règle S1210) ; l'égalité vient du record struct.
    public static bool operator <(SemanticVersionCore left, SemanticVersionCore right) => left.CompareTo(right) < 0;
    public static bool operator <=(SemanticVersionCore left, SemanticVersionCore right) => left.CompareTo(right) <= 0;
    public static bool operator >(SemanticVersionCore left, SemanticVersionCore right) => left.CompareTo(right) > 0;
    public static bool operator >=(SemanticVersionCore left, SemanticVersionCore right) => left.CompareTo(right) >= 0;

    public override string ToString()
        => Tag == PreReleaseTag.None
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{TagLabel()}.{TagNumber}";

    // Appelée uniquement quand Tag != None (voir ToString) : le seul cas restant possible.
    private string TagLabel() => Tag switch
    {
        PreReleaseTag.Dev => "dev",
        PreReleaseTag.Pre => "pre",
        PreReleaseTag.Rc => "rc",
        _ => throw new UnreachableException($"Pas d'étiquette textuelle pour {Tag}."),
    };
}