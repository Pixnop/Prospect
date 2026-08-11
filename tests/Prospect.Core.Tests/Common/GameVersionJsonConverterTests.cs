using System.Text.Json;
using System.Text.Json.Serialization;

using Prospect.Core.Common;

using Shouldly;

namespace Prospect.Core.Tests.Common;

/// <summary>
/// Vérifie que <see cref="GameVersion"/> se sérialise en chaîne JSON et se désérialise à
/// l'identique, via un <see cref="JsonSerializerContext"/> source-gen minimal : c'est la preuve
/// que le converter fonctionne sans réflexion, dans les mêmes conditions qu'un vrai schéma
/// persistant (instance.json expose "gameVersion" comme un champ d'un objet plus large, pas
/// comme un document racine).
/// </summary>
public class GameVersionJsonConverterTests
{
    [Fact]
    public void Serialize_RootValue_WritesCanonicalString()
    {
        var version = GameVersion.Parse("1.22.0-rc.10");

        var json = JsonSerializer.Serialize(version, GameVersionTestJsonContext.Default.GameVersion);

        json.ShouldBe("\"1.22.0-rc.10\"");
    }

    [Fact]
    public void Deserialize_RootValue_ReturnsEquivalentVersion()
    {
        var version = JsonSerializer.Deserialize("\"1.21.3\"", GameVersionTestJsonContext.Default.GameVersion);

        version.ShouldBe(GameVersion.Parse("1.21.3"));
    }

    [Fact]
    public void Deserialize_InvalidString_ThrowsJsonException()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize("\"not-a-version\"", GameVersionTestJsonContext.Default.GameVersion));
    }

    [Fact]
    public void RoundTrip_NestedInsideDocument_PreservesValue()
    {
        var holder = new GameVersionHolder("Homestead 1.21", GameVersion.Parse("1.21.3"));

        var json = JsonSerializer.Serialize(holder, GameVersionTestJsonContext.Default.GameVersionHolder);
        var roundTripped = JsonSerializer.Deserialize(json, GameVersionTestJsonContext.Default.GameVersionHolder);

        json.ShouldContain("\"gameVersion\":\"1.21.3\"");
        roundTripped.ShouldNotBeNull();
        roundTripped!.GameVersion.ShouldBe(holder.GameVersion);
    }
}

internal sealed record GameVersionHolder(string Name, [property: JsonPropertyName("gameVersion")] GameVersion GameVersion);

[JsonSerializable(typeof(GameVersion))]
[JsonSerializable(typeof(GameVersionHolder))]
internal sealed partial class GameVersionTestJsonContext : JsonSerializerContext;