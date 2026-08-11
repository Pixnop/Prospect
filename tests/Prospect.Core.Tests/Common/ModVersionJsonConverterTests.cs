using System.Text.Json;
using System.Text.Json.Serialization;

using Prospect.Core.Common;

using Shouldly;

namespace Prospect.Core.Tests.Common;

/// <summary>
/// Vérifie que <see cref="ModVersion"/> se sérialise en chaîne JSON et se désérialise à
/// l'identique, via un <see cref="JsonSerializerContext"/> source-gen minimal (voir
/// <see cref="GameVersionJsonConverterTests"/> pour le pendant côté version de jeu).
/// </summary>
public class ModVersionJsonConverterTests
{
    [Fact]
    public void Serialize_RootValue_WritesCanonicalString()
    {
        var version = ModVersion.Parse("1.8.0");

        var json = JsonSerializer.Serialize(version, ModVersionTestJsonContext.Default.ModVersion);

        json.ShouldBe("\"1.8.0\"");
    }

    [Fact]
    public void Deserialize_RootValue_ReturnsEquivalentVersion()
    {
        var version = JsonSerializer.Deserialize("\"1.12.0\"", ModVersionTestJsonContext.Default.ModVersion);

        version.ShouldBe(ModVersion.Parse("1.12.0"));
    }

    [Fact]
    public void Deserialize_InvalidString_ThrowsJsonException()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize("\"not-a-version\"", ModVersionTestJsonContext.Default.ModVersion));
    }

    [Fact]
    public void RoundTrip_NestedInsideDocument_PreservesValue()
    {
        var holder = new ModVersionHolder("carrycapacity", ModVersion.Parse("1.8.0"));

        var json = JsonSerializer.Serialize(holder, ModVersionTestJsonContext.Default.ModVersionHolder);
        var roundTripped = JsonSerializer.Deserialize(json, ModVersionTestJsonContext.Default.ModVersionHolder);

        json.ShouldContain("\"version\":\"1.8.0\"");
        roundTripped.ShouldNotBeNull();
        roundTripped!.Version.ShouldBe(holder.Version);
    }
}

internal sealed record ModVersionHolder(string ModId, [property: JsonPropertyName("version")] ModVersion Version);

[JsonSerializable(typeof(ModVersion))]
[JsonSerializable(typeof(ModVersionHolder))]
internal sealed partial class ModVersionTestJsonContext : JsonSerializerContext;