using System.Text;
using System.Text.Json;

using Prospect.Core.Common;

namespace Prospect.Core.ModDb;

/// <summary>
/// Lecture tolérante d'un <c>modinfo.json</c>. Dans la nature, ces fichiers sont écrits à la main
/// par des auteurs différents : la casse des clés varie (<c>modid</c>, <c>Modid</c>, <c>ModID</c>),
/// certains portent des commentaires et des virgules terminales, l'indentation n'est jamais la
/// même d'un mod à l'autre. VS Launcher les lisait en JSON5 ; ici, <c>System.Text.Json</c> suffit
/// avec <see cref="JsonCommentHandling.Skip"/>, <c>AllowTrailingCommas</c> et une résolution de
/// clés insensible à la casse.
/// </summary>
/// <remarks>
/// Le parcours passe par <see cref="JsonDocument"/> plutôt que par une désérialisation vers un
/// record : c'est le seul moyen de résoudre les clés sans tenir compte de la casse ET de tolérer
/// qu'un champ arrive dans un type inattendu (une chaîne là où un tableau est attendu, un nombre
/// là où une chaîne est attendue). Un désérialiseur strict rejetterait le fichier entier pour un
/// seul champ mal typé, ce qui est exactement le contraire du comportement voulu.
/// </remarks>
public static class ModInfoParser
{
    private static readonly JsonDocumentOptions TolerantOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Les trois identifiants que le ModDB lui-même sort de la résolution mod-à-mod
    // (IGNORED_AUTO_IDENTIFIERS, lib/relations.php).
    private static readonly HashSet<string> SpecialDependencyIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "survival", "creative",
    };

    /// <summary>Nom canonique du fichier de métadonnées, à la racine de l'archive.</summary>
    public const string FileName = "modinfo.json";

    /// <summary>Icône cherchée par défaut quand <c>iconPath</c> n'est pas déclaré.</summary>
    public const string DefaultIconFileName = "modicon.png";

    /// <summary>
    /// Vrai pour <c>game</c>, <c>survival</c> et <c>creative</c>, qui ne sont pas de vrais mods à
    /// résoudre : <c>game</c> alimente la compatibilité de version de jeu, les deux autres
    /// désignent du contenu livré avec le jeu.
    /// </summary>
    public static bool IsSpecialDependencyId(string identifier) => SpecialDependencyIds.Contains(identifier);

    /// <summary>
    /// Lit le contenu d'un <c>modinfo.json</c>.
    /// </summary>
    /// <param name="json">Contenu du fichier.</param>
    /// <returns>
    /// Un <see cref="ModInfoResult"/> porteur soit des métadonnées, soit d'une raison. Cette
    /// méthode ne lève jamais pour un contenu malformé : c'est tout l'intérêt du type de retour.
    /// </returns>
    public static ModInfoResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ModInfoResult.Failure(ModInfoProblem.MalformedJson);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, TolerantOptions);
        }
        catch (JsonException)
        {
            return ModInfoResult.Failure(ModInfoProblem.MalformedJson);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ModInfoResult.Failure(ModInfoProblem.MalformedJson);
            }

            return Build(document.RootElement);
        }
    }

    /// <summary>
    /// Identifiant dérivé du nom, comme le fait le jeu quand <c>modid</c> est absent : minuscules,
    /// uniquement lettres et chiffres. Rend une chaîne vide si le nom ne contient rien d'utilisable.
    /// </summary>
    public static string DeriveModId(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static ModInfoResult Build(JsonElement root)
    {
        var name = ReadString(root, "name") ?? string.Empty;
        var declaredModId = ReadString(root, "modid");
        var modId = string.IsNullOrWhiteSpace(declaredModId) ? DeriveModId(name) : declaredModId.Trim();

        if (string.IsNullOrEmpty(modId))
        {
            // Ni identifiant ni nom exploitable : le fichier est syntaxiquement valide mais ne
            // décrit aucun mod reconnaissable.
            return ModInfoResult.Failure(ModInfoProblem.MissingIdentity);
        }

        var rawVersion = ReadString(root, "version");
        var (dependencies, unreadable) = ReadDependencies(root);
        dependencies.TryGetValue("game", out var gameRequirement);

        return ModInfoResult.Success(new ModInfo
        {
            ModId = modId,
            Name = string.IsNullOrWhiteSpace(name) ? modId : name.Trim(),
            Version = ModVersion.TryParse(rawVersion, out var version) ? version : null,
            RawVersion = rawVersion,
            Description = ReadString(root, "description") ?? string.Empty,
            Authors = ReadStringList(root, "authors"),
            Contributors = ReadStringList(root, "contributors"),
            Side = ParseSide(ReadString(root, "side")),
            Type = ParseType(ReadString(root, "type")),
            RequiredOnClient = ReadBoolean(root, "requiredOnClient") ?? true,
            RequiredOnServer = ReadBoolean(root, "requiredOnServer") ?? true,
            Dependencies = dependencies,
            UnreadableDependencies = unreadable,
            IconPath = ReadString(root, "iconPath"),
            Website = ReadString(root, "website"),
            GameRequirement = dependencies.ContainsKey("game") ? gameRequirement : null,
        });
    }

    /// <summary>
    /// Traduit le champ <c>side</c>. La casse est libre : les échantillons réels écrivent
    /// <c>"universal"</c> et <c>"client"</c> en minuscules alors que la doc de la classe du jeu
    /// écrit les valeurs en PascalCase, parce que la désérialisation côté jeu est elle-même
    /// insensible à la casse.
    /// </summary>
    private static ModSide ParseSide(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "client" => ModSide.Client,
        "server" => ModSide.Server,
        _ => ModSide.Universal,
    };

    private static ModType ParseType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "theme" => ModType.Theme,
        "content" => ModType.Content,
        "code" => ModType.Code,
        _ => ModType.Unknown,
    };

    // dependencies est un OBJET {modid: version}, pas un tableau d'objets : confirmé sur les trois
    // échantillons réels. Chaque valeur est une borne minimale, "" et "*" valant « toute version ».
    private static (Dictionary<string, VersionRequirement> Dependencies, IReadOnlyList<string> Unreadable) ReadDependencies(JsonElement root)
    {
        var dependencies = new Dictionary<string, VersionRequirement>(StringComparer.OrdinalIgnoreCase);
        var unreadable = new List<string>();

        if (!TryGetProperty(root, "dependencies", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return (dependencies, unreadable);
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                continue;
            }

            var raw = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null => string.Empty,
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => null,
            };

            if (raw is not null && VersionRequirement.TryParse(raw, out var requirement))
            {
                dependencies[property.Name.Trim()] = requirement;
            }
            else
            {
                // Une contrainte illisible ne doit pas silencieusement devenir « toute version » :
                // ce serait affirmer une compatibilité que personne n'a vérifiée.
                unreadable.Add($"{property.Name.Trim()}: {raw ?? property.Value.GetRawText()}");
            }
        }

        return (dependencies, unreadable);
    }

    private static string? ReadString(JsonElement root, string propertyName)
        => TryGetProperty(root, propertyName, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool? ReadBoolean(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            // Certains fichiers écrivent "true" entre guillemets : le jeu s'en accommode, nous aussi.
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    // authors doit être un tableau d'après le wiki, mais une chaîne seule se rencontre : la
    // tolérer coûte trois lignes et évite de perdre l'information.
    private static string[] ReadStringList(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var element))
        {
            return [];
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var single = element.GetString();

            return string.IsNullOrWhiteSpace(single) ? [] : [single.Trim()];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
    }

    // System.Text.Json ne sait pas résoudre une propriété sans tenir compte de la casse sur un
    // JsonElement : le parcours explicite est le prix de la tolérance sur modid/Modid/ModID.
    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;

                return true;
            }
        }

        value = default;

        return false;
    }
}