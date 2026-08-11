using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Prospect.Core.Instances;

/// <summary>
/// Dérive un slug de dossier stable à partir du nom d'une instance (docs/architecture.md,
/// arborescence disque, ex. <c>homestead-121/</c>) : minuscules, diacritiques repliés, tout
/// caractère hors <c>[a-z0-9]</c> remplacé par un tiret, tirets consécutifs fusionnés, bornes
/// nettoyées, longueur bornée à <see cref="MaxLength"/>, repli sur <c>"instance"</c> si le
/// résultat est vide. Fonctions pures et statiques (aucun état à porter) : vérifier qu'un slug
/// est déjà pris relève de la topologie disque, donc de l'appelant, passée ici via un simple
/// prédicat plutôt qu'une dépendance au système de fichiers.
/// </summary>
public static partial class InstanceSlugGenerator
{
    private const int MaxLength = 40;
    private const string FallbackSlug = "instance";

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharacters();

    [GeneratedRegex("-+")]
    private static partial Regex ConsecutiveDashes();

    /// <summary>Calcule le slug d'un nom, sans résolution d'unicité.</summary>
    public static string Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return FallbackSlug;
        }

        var folded = RemoveDiacritics(name).ToLowerInvariant();
        var dashed = NonSlugCharacters().Replace(folded, "-");
        var merged = ConsecutiveDashes().Replace(dashed, "-").Trim('-');
        var bounded = merged.Length > MaxLength ? merged[..MaxLength].TrimEnd('-') : merged;

        return bounded.Length == 0 ? FallbackSlug : bounded;
    }

    /// <summary>
    /// Calcule un slug unique à partir de <paramref name="name"/> : le slug de base s'il est
    /// libre, sinon un suffixe numérique croissant à partir de <c>-2</c> tant que
    /// <paramref name="slugExists"/> répond vrai.
    /// </summary>
    /// <param name="name">Nom source, passé à <see cref="Slugify"/>.</param>
    /// <param name="slugExists">Prédicat consulté pour chaque candidat (typiquement un dossier déjà présent sur disque).</param>
    public static string GenerateUnique(string? name, Func<string, bool> slugExists)
    {
        ArgumentNullException.ThrowIfNull(slugExists);

        var baseSlug = Slugify(name);
        if (!slugExists(baseSlug))
        {
            return baseSlug;
        }

        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{baseSlug}-{suffix}";
            suffix++;
        }
        while (slugExists(candidate));

        return candidate;
    }

    // Décomposition canonique (NFD) puis suppression des marques diacritiques combinantes : la
    // technique standard pour replier "É" en "E" indépendamment de la table de caractères visée.
    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}