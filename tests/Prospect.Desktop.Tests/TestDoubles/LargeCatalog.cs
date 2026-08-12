using System.Globalization;
using System.Text;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Générateur de catalogue ModDB à l'échelle réelle. Le vrai <c>/api/mods</c> rend ~8 000 fiches en
/// un seul appel, sans pagination (docs/research/moddb-api.md) : les doubles à deux ou trois mods
/// des autres tests ne peuvent structurellement pas révéler ce que devient la grille à cette
/// échelle, ni combien de cartes elle réalise.
/// </summary>
/// <remarks>
/// Les entrées reproduisent la répartition mesurée sur le dépôt réel plutôt qu'un modèle uniforme :
/// un tiers sans logo, une fiche sur dix sans résumé, des tags répartis sur un vocabulaire large,
/// et des dates de dernière release échelonnées, pour que tri et filtres travaillent sur des
/// données aussi variées qu'en production.
/// </remarks>
internal static class LargeCatalog
{
    /// <summary>Vocabulaire de tags utilisé par le générateur, distribué sur les fiches.</summary>
    public static readonly IReadOnlyList<string> TagNames =
    [
        "Worldgen", "Exploration", "Utility", "Crafting", "Cosmetics", "Library",
        "QoL", "Storage", "Combat", "Food", "Tweak", "Magic",
    ];

    /// <summary>Corps JSON de <c>/api/mods</c> avec <paramref name="count"/> fiches.</summary>
    public static string CatalogJson(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var builder = new StringBuilder(count * 320);
        builder.Append("{ \"statuscode\": \"200\", \"mods\": [");

        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendMod(builder, index);
        }

        return builder.Append("] }").ToString();
    }

    /// <summary>Corps JSON de <c>/api/tags</c> avec <paramref name="count"/> catégories, comme le vocabulaire réel (plus de 200).</summary>
    public static string TagsJson(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var builder = new StringBuilder(count * 64);
        builder.Append("{ \"statuscode\": \"200\", \"tags\": [");

        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var name = index < TagNames.Count ? TagNames[index] : Invariant($"Catégorie {index}");
            builder.Append(Invariant($"{{\"tagid\":\"{index + 1}\",\"name\":\"{name}\",\"color\":\"#92C96AFF\"}}"));
        }

        return builder.Append("] }").ToString();
    }

    private static void AppendMod(StringBuilder builder, int index)
    {
        var modId = index + 1;
        var hasLogo = index % 3 != 0;
        var logo = hasLogo
            ? Invariant($"\"https://moddbcdn.vintagestory.at/logo-{modId}.png\"")
            : "null";
        var summary = index % 10 == 0 ? string.Empty : Invariant($"Résumé du mod de test numéro {modId}.");

        // Zéro, un ou deux tags, comme le catalogue réel où 10 % des fiches n'en ont aucun (et où
        // /api/mods rend alors [""] plutôt que [], d'où la chaîne vide reproduite ici).
        var tags = (index % 10) switch
        {
            0 => "[\"\"]",
            < 5 => Invariant($"[\"{TagNames[index % TagNames.Count]}\"]"),
            _ => Invariant($"[\"{TagNames[index % TagNames.Count]}\",\"{TagNames[(index + 4) % TagNames.Count]}\"]"),
        };

        var lastReleased = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            .AddHours(-index)
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var downloads = Math.Max(0, 900_000 - (index * 97));

        builder.Append(Invariant(
            $$"""
            { "modid": {{modId}}, "assetid": {{modId + 10_000}}, "downloads": {{downloads}},
              "follows": {{index % 500}}, "trendingpoints": {{index % 17}}, "comments": {{index % 40}},
              "name": "Mod de test {{modId}}", "summary": "{{summary}}", "modidstrs": ["testmod{{modId}}"],
              "author": "Auteur {{index % 250}}", "urlalias": null, "side": "both", "type": "mod",
              "logo": {{logo}}, "tags": {{tags}}, "lastreleased": "{{lastReleased}}" }
            """));
    }

    private static string Invariant(FormattableString value) => FormattableString.Invariant(value);
}