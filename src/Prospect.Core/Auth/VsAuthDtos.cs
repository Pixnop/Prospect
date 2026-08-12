using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prospect.Core.Auth;

/// <summary>
/// Réponse de <c>POST auth3.vintagestory.at/v2/gamelogin</c>, telle que le code de VS Launcher la
/// lit (docs/research/vslauncher-et-distribution.md, section a). DTO défensif : l'éditeur ne
/// documente pas ce contrat, aucun champ n'est donc déclaré obligatoire et tout ce qui finira en
/// chaîne est lu par <see cref="LenientStringJsonConverter"/>, pour qu'un <c>uid</c> qui
/// deviendrait numérique un jour ne fasse pas échouer une connexion parfaitement valide.
/// </summary>
internal sealed record VsGameLoginResponseDto
{
    /// <summary>
    /// Verdict du service : <c>1</c> pour une session établie. Lu aussi depuis une chaîne, parce
    /// que le code JavaScript d'origine compare avec <c>==</c> et ne distingue donc pas
    /// <c>0</c> de <c>"0"</c>.
    /// </summary>
    [JsonPropertyName("valid")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? Valid { get; init; }

    /// <summary>Motif du refus : <c>requiretotpcode</c>, <c>invalidemailorpassword</c>, <c>wrongtotpcode</c>.</summary>
    [JsonPropertyName("reason")]
    [JsonConverter(typeof(LenientStringJsonConverter))]
    public string? Reason { get; init; }

    /// <summary>Jeton de pré-connexion à réinjecter dans la deuxième passe.</summary>
    [JsonPropertyName("prelogintoken")]
    [JsonConverter(typeof(LenientStringJsonConverter))]
    public string? PreLoginToken { get; init; }

    [JsonPropertyName("sessionkey")]
    [JsonConverter(typeof(LenientStringJsonConverter))]
    public string? SessionKey { get; init; }

    [JsonPropertyName("sessionsignature")]
    [JsonConverter(typeof(LenientStringJsonConverter))]
    public string? SessionSignature { get; init; }

    [JsonPropertyName("mptoken")]
    [JsonConverter(typeof(LenientStringJsonConverter))]
    public string? MpToken { get; init; }

    [JsonPropertyName("uid")]
    [JsonConverter(typeof(LenientStringJsonConverter))]
    public string? Uid { get; init; }

    [JsonPropertyName("entitlements")]
    [JsonConverter(typeof(LenientStringJsonConverter))]
    public string? Entitlements { get; init; }

    [JsonPropertyName("playername")]
    [JsonConverter(typeof(LenientStringJsonConverter))]
    public string? PlayerName { get; init; }

    /// <summary>
    /// Droit d'héberger un serveur. Booléen dans les types de VS Launcher, mais destiné à un
    /// dictionnaire de chaînes côté jeu : lu tel quel, converti en chaîne ici.
    /// </summary>
    [JsonPropertyName("hasgameserver")]
    [JsonConverter(typeof(LenientStringJsonConverter))]
    public string? HasGameServer { get; init; }
}

/// <summary>
/// Lit une valeur JSON scalaire quelconque et la rend en chaîne : chaîne telle quelle, booléen en
/// <c>true</c>/<c>false</c>, nombre dans sa forme d'origine, <c>null</c> en <see langword="null"/>.
/// Existe parce qu'un contrat non documenté peut changer la forme d'un champ sans prévenir, et
/// qu'échouer à lire une réponse par ailleurs valide serait le pire des comportements pour une
/// connexion de compte.
/// </summary>
internal sealed class LenientStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Valeur scalaire attendue, jeton {reader.TokenType} reçu."),
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value);
    }

    // Un entier reste un entier (pas de « 4815162342 » qui deviendrait « 4.81516E+09 ») ; tout le
    // reste passe par la forme invariante du double.
    private static string ReadNumber(ref Utf8JsonReader reader)
        => reader.TryGetInt64(out var integer)
            ? integer.ToString(CultureInfo.InvariantCulture)
            : reader.GetDouble().ToString(CultureInfo.InvariantCulture);
}