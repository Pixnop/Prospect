using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Prospect.Core.ModDb;

/// <summary>
/// Lit une map <c>clé -> valeur</c> du ModDB en acceptant les DEUX formes que le serveur produit
/// pour la même chose : un objet JSON quand la map porte des entrées, un TABLEAU VIDE quand elle
/// n'en porte aucune.
/// </summary>
/// <remarks>
/// <para>
/// Ce n'est pas une bizarrerie du ModDB mais de PHP, et elle est structurelle : le langage n'a
/// qu'un seul type pour la liste et le dictionnaire, et <c>json_encode</c> tranche à l'encodage
/// selon les clés présentes. Une map peuplée de <c>modidstr</c> sort en <c>{"carryon": {...}}</c>,
/// la même map vide sort en <c>[]</c> parce que plus rien ne la distingue d'une liste vide. Aucun
/// endpoint ne peut donc promettre « toujours un objet ».
/// </para>
/// <para>
/// Le défaut de terrain qui a mené ici : <c>GET /api/updates</c> sur un lot dont RIEN n'est en
/// retard répond exactement <c>{"statuscode":"200","updates":[]}</c>, relevé en direct. La
/// désérialisation attendait un objet, levait une <see cref="JsonException"/>, que
/// <c>ModDbClient.Deserialize</c> traduisait en <c>ModDbUnavailableException</c> : « Le ModDB est
/// injoignable et aucun relevé en cache n'est disponible », affiché entre deux téléchargements
/// ModDB parfaitement réussis. Le cas le plus banal (tout est à jour) était donc le seul à
/// échouer, et il échouait en accusant le réseau.
/// </para>
/// <para>
/// Un tableau NON VIDE reste refusé. Il n'a pas de clés, donc rien n'en ferait une map : le
/// convertir en map vide reviendrait à jeter des données en silence, ce qui est pire qu'une erreur
/// franche. La forme n'a jamais été observée et n'est pas produisible par <c>json_encode</c> sur
/// une map dont les clés sont des <c>modidstr</c>.
/// </para>
/// <para>
/// Le <see langword="null"/> JSON n'est traité NULLE PART ici, et c'est le comportement par défaut
/// du sérialiseur plutôt qu'un oubli : <c>HandleNull</c> vaut faux pour un type référence, donc un
/// champ absent ou nul rend <see langword="null"/> sans jamais passer par ce convertisseur. Le
/// distinguer de la map vide reste utile en aval : l'absence signale une réponse d'une forme qu'on
/// ne connaît pas, la map vide un lot dont rien n'est en retard.
/// </para>
/// </remarks>
/// <typeparam name="TValue">Type des valeurs de la map.</typeparam>
internal sealed class PhpAssociativeArrayConverter<TValue> : JsonConverter<Dictionary<string, TValue>>
{
    /// <inheritdoc />
    public override Dictionary<string, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.StartArray => ReadEmptyArray(ref reader),
            JsonTokenType.StartObject => ReadObject(ref reader, options),
            _ => throw new JsonException($"Une map du ModDB ne peut être qu'un objet ou un tableau vide, pas un {reader.TokenType}."),
        };

    /// <inheritdoc />
    /// <remarks>
    /// L'écriture rend toujours un OBJET, y compris pour une map vide : nos propres documents (le
    /// cache disque, les manifestes) n'ont aucune raison de reproduire l'ambiguïté de PHP, et
    /// <see cref="Read"/> relit un objet vide sans difficulté.
    /// </remarks>
    public override void Write(Utf8JsonWriter writer, Dictionary<string, TValue> value, JsonSerializerOptions options)
    {
        var typeInfo = ValueTypeInfo(options);

        writer.WriteStartObject();
        foreach (var (key, item) in value)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, item, typeInfo);
        }

        writer.WriteEndObject();
    }

    // Aucune garde contre un document tronqué ici, et c'est délibéré : le sérialiseur valide le
    // document AVANT d'appeler un convertisseur, donc un tableau ouvert et jamais fermé lève bien
    // avant d'arriver jusqu'ici. Les écrire quand même produirait des branches qu'aucun test ne
    // peut atteindre.
    private static Dictionary<string, TValue> ReadEmptyArray(ref Utf8JsonReader reader)
    {
        reader.Read();

        return reader.TokenType == JsonTokenType.EndArray
            ? []
            : throw new JsonException("Un tableau NON VIDE ne peut pas être lu comme une map du ModDB : il n'a pas de clés.");
    }

    private static Dictionary<string, TValue> ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var typeInfo = ValueTypeInfo(options);
        var result = new Dictionary<string, TValue>(StringComparer.Ordinal);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString()!;
            reader.Read();
            result[key] = JsonSerializer.Deserialize(ref reader, typeInfo)!;
        }

        return result;
    }

    // Le type d'info vient du contexte source-gen porté par les options : aucune réflexion, donc
    // rien qui casserait un jour sous trimming (docs/architecture.md, « System.Text.Json avec
    // générateurs de source »).
    private static JsonTypeInfo<TValue> ValueTypeInfo(JsonSerializerOptions options)
        => (JsonTypeInfo<TValue>)options.GetTypeInfo(typeof(TValue));
}