namespace Prospect.Core.ModDb;

/// <summary>Critère de tri du navigateur de mods (design/ui_kits/launcher/screen-mods.jsx).</summary>
public enum ModCatalogSort
{
    /// <summary>Les plus téléchargés d'abord, le tri par défaut de l'écran.</summary>
    Downloads,

    /// <summary>Les dernières releases d'abord.</summary>
    RecentlyUpdated,

    /// <summary>Score de tendance du site, souvent nul : ne rend un ordre utile que sur les mods actifs.</summary>
    Trending,

    /// <summary>Ordre alphabétique.</summary>
    Name,
}

/// <summary>Filtrage de compatibilité demandé par l'utilisateur.</summary>
public enum ModCompatibilityFilter
{
    /// <summary>Aucun filtre : tout le catalogue, sans badge.</summary>
    All,

    /// <summary>Seulement les mods compatibles avec la version cible.</summary>
    CompatibleOnly,
}

/// <summary>
/// Une recherche dans le catalogue, appliquée ENTIÈREMENT en mémoire par
/// <see cref="ModCatalogSearch"/>. Il n'existe aucune pagination côté serveur et le catalogue
/// entier tient déjà en mémoire après un appel : refaire un aller-retour réseau à chaque frappe
/// serait à la fois plus lent et plus impoli.
/// </summary>
/// <param name="Text">Sous-chaîne cherchée dans le nom, le résumé et l'auteur. Vide pour ne pas filtrer.</param>
/// <param name="TagName">Catégorie exigée, ou <see langword="null"/> pour toutes.</param>
/// <param name="Sort">Ordre de présentation.</param>
/// <param name="Compatibility">Filtre de compatibilité, effectif seulement si un index est fourni.</param>
public sealed record ModCatalogQuery(
    string Text = "",
    string? TagName = null,
    ModCatalogSort Sort = ModCatalogSort.Downloads,
    ModCompatibilityFilter Compatibility = ModCompatibilityFilter.All);

/// <summary>
/// Applique une <see cref="ModCatalogQuery"/> à un catalogue déjà chargé. Pur calcul, sans
/// dépendance : c'est ce qui rend la recherche du navigateur de mods testable sans réseau ni UI.
/// </summary>
public static class ModCatalogSearch
{
    /// <summary>
    /// Filtre et trie <paramref name="mods"/>.
    /// </summary>
    /// <param name="mods">Catalogue complet.</param>
    /// <param name="query">Critères.</param>
    /// <param name="compatibility">
    /// Index de compatibilité, ou <see langword="null"/> quand aucune instance cible n'est
    /// choisie. Un index vide (API injoignable) ne filtre rien : mieux vaut tout montrer sans
    /// badge que masquer le catalogue à cause d'une donnée manquante.
    /// </param>
    public static IReadOnlyList<ModDbModSummary> Apply(
        IReadOnlyList<ModDbModSummary> mods,
        ModCatalogQuery query,
        ModDbCompatibilityIndex? compatibility = null)
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(query);

        var filtered = mods.Where(mod => MatchesText(mod, query.Text) && MatchesTag(mod, query.TagName));

        if (query.Compatibility == ModCompatibilityFilter.CompatibleOnly && compatibility is { ModIds.Count: > 0 })
        {
            filtered = filtered.Where(mod => compatibility.Contains(mod.ModId));
        }

        return Sort(filtered, query.Sort).ToArray();
    }

    private static IEnumerable<ModDbModSummary> Sort(IEnumerable<ModDbModSummary> mods, ModCatalogSort sort) => sort switch
    {
        ModCatalogSort.RecentlyUpdated => mods
            .OrderByDescending(mod => mod.LastReleasedUtc ?? DateTimeOffset.MinValue)
            .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase),
        ModCatalogSort.Trending => mods
            .OrderByDescending(mod => mod.TrendingPoints)
            .ThenByDescending(mod => mod.Downloads),
        ModCatalogSort.Name => mods.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase),
        _ => mods
            .OrderByDescending(mod => mod.Downloads)
            .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase),
    };

    private static bool MatchesText(ModDbModSummary mod, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var needle = text.Trim();

        return mod.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || mod.Summary.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || mod.Author.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || mod.ModIdStrings.Any(identifier => identifier.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesTag(ModDbModSummary mod, string? tagName)
        => string.IsNullOrWhiteSpace(tagName)
            || mod.Tags.Any(tag => string.Equals(tag, tagName, StringComparison.OrdinalIgnoreCase));
}