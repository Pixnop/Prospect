using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prospect.Core.Common;

/// <summary>
/// Sérialise une <see cref="GameVersion"/> comme une simple chaîne JSON (sa forme canonique
/// <see cref="GameVersion.ToString"/>), plutôt que comme un objet avec ses composantes. C'est le
/// format attendu partout où une version de jeu apparaît dans nos schémas (le champ
/// <c>gameVersion</c> d'<c>instance.json</c> par exemple). Ne fait aucune réflexion : compatible
/// avec un <see cref="JsonSerializerContext"/> source-gen dès lors que le type porte
/// l'attribut <see cref="JsonConverterAttribute"/> qui pointe ici (voir <see cref="GameVersion"/>).
/// </summary>
public sealed class GameVersionJsonConverter : JsonConverter<GameVersion>
{
    /// <inheritdoc />
    public override GameVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null || !GameVersion.TryParse(text, out var version))
        {
            throw new JsonException($"'{text}' n'est pas une version de jeu valide.");
        }

        return version;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, GameVersion value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}