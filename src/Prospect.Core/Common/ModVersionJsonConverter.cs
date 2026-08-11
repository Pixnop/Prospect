using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prospect.Core.Common;

/// <summary>
/// Sérialise une <see cref="ModVersion"/> comme une simple chaîne JSON (sa forme canonique
/// <see cref="ModVersion.ToString"/>), plutôt que comme un objet avec ses composantes. C'est le
/// format attendu partout où une version de mod apparaît dans nos schémas (le champ
/// <c>version</c> d'une entrée de modpack par exemple). Ne fait aucune réflexion : compatible
/// avec un <see cref="JsonSerializerContext"/> source-gen dès lors que le type porte
/// l'attribut <see cref="JsonConverterAttribute"/> qui pointe ici (voir <see cref="ModVersion"/>).
/// </summary>
public sealed class ModVersionJsonConverter : JsonConverter<ModVersion>
{
    /// <inheritdoc />
    public override ModVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null || !ModVersion.TryParse(text, out var version))
        {
            throw new JsonException($"'{text}' n'est pas une version de mod valide.");
        }

        return version;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ModVersion value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}