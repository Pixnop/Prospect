namespace Prospect.Core.Common;

/// <summary>
/// Contrainte de version telle qu'elle apparaît dans une entrée <c>dependencies</c> de
/// modinfo.json (docs/research/moddb-api.md, section « dependencies »). Une chaîne vide,
/// <c>"*"</c> ou absente signifie « toute version » ; toute autre valeur est une BORNE
/// MINIMALE, comparée sémantiquement (<c>&gt;=</c>). Il n'existe pas de wildcard <c>1.20.*</c>
/// dans cette grammaire : la recherche l'a établi contre le wiki, la doc de l'assembly du jeu
/// et des échantillons réels, donc une chaîne qui y ressemble est un format invalide plutôt
/// qu'une syntaxe tolérée.
/// </summary>
/// <remarks>
/// Un <c>dependencies</c> de modinfo.json peut viser aussi bien un autre mod (comparaison
/// contre un <see cref="ModVersion"/> installé) que le jeu lui-même via la clé spéciale
/// <c>game</c> (comparaison contre le <see cref="GameVersion"/> de l'instance) : c'est pour ça
/// que ce type expose une <see cref="IsSatisfiedBy(ModVersion)"/> et une
/// <see cref="IsSatisfiedBy(GameVersion)"/> plutôt que de se lier à l'un des deux types de
/// version. La borne minimale elle-même est stockée sous la forme du cœur interne partagé
/// (<see cref="SemanticVersionCore"/>), jamais sous la forme d'un <see cref="ModVersion"/> ou
/// <see cref="GameVersion"/> : ça évite d'avoir à choisir arbitrairement l'un des deux types
/// publics pour représenter une contrainte qui peut s'appliquer aux deux.
/// </remarks>
public readonly record struct VersionRequirement : IEquatable<VersionRequirement>
{
    private readonly SemanticVersionCore? _minimum;

    private VersionRequirement(SemanticVersionCore? minimum) => _minimum = minimum;

    /// <summary>Vrai si la contrainte accepte n'importe quelle version (pas de borne minimale).</summary>
    public bool IsAny => _minimum is null;

    /// <summary>
    /// Parse une entrée <c>dependencies</c> de modinfo.json. Accepte <see langword="null"/>,
    /// une chaîne vide ou <c>"*"</c> comme « toute version ». Le parsing de la borne minimale
    /// est tolérant (préfixe <c>v</c>/<c>V</c>, espaces de bordure) : comme le reste d'un
    /// modinfo.json glané dans la nature, cette valeur n'est pas toujours propre.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="value"/> n'est ni un joker ni une version valide.</exception>
    public static VersionRequirement Parse(string? value)
    {
        if (!TryParse(value, out var requirement))
        {
            throw new FormatException(
                $"'{value}' n'est pas une contrainte de version valide (attendu : vide, \"*\", ou Major.Minor.Patch[-dev|pre|rc.N]).");
        }

        return requirement;
    }

    /// <summary>Variante non lançante de <see cref="Parse"/>.</summary>
    public static bool TryParse(string? value, out VersionRequirement result)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed == "*")
        {
            result = new VersionRequirement(null);
            return true;
        }

        if (SemanticVersionCore.TryParseLenient(trimmed, out var core))
        {
            result = new VersionRequirement(core);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// La contrainte est satisfaite si elle accepte toute version, ou si
    /// <paramref name="version"/> est supérieure ou égale à la borne minimale.
    /// </summary>
    public bool IsSatisfiedBy(ModVersion version) => _minimum is null || version.Core.CompareTo(_minimum.Value) >= 0;

    /// <summary>
    /// La contrainte est satisfaite si elle accepte toute version, ou si
    /// <paramref name="version"/> est supérieure ou égale à la borne minimale. Utile pour la
    /// dépendance spéciale <c>game</c> d'un modinfo.json.
    /// </summary>
    public bool IsSatisfiedBy(GameVersion version) => _minimum is null || version.Core.CompareTo(_minimum.Value) >= 0;

    /// <inheritdoc />
    public override string ToString() => _minimum?.ToString() ?? "*";
}