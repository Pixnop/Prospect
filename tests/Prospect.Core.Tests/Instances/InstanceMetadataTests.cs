using System.Text.Json;

using Prospect.Core.Common;
using Prospect.Core.Instances;

using Shouldly;

namespace Prospect.Core.Tests.Instances;

/// <summary>
/// Vérifie le schéma v1 d'<c>instance.json</c> (docs/architecture.md) via le contexte source-gen
/// de production <see cref="InstanceJsonContext"/> : round-trip complet, casse camelCase des
/// clés, et valeurs par défaut d'une instance construite sans tous les champs optionnels.
/// </summary>
public class InstanceMetadataTests
{
    private static InstanceMetadata CreateSample() => new()
    {
        SchemaVersion = InstanceMetadata.CurrentSchemaVersion,
        Id = Guid.Parse("0c9c1f57-8b2e-4f2a-9c41-3d8a12f7b6e0"),
        Name = "Homestead 1.21",
        GameVersion = GameVersion.Parse("1.21.3"),
        Icon = "builtin:default",
        CreatedUtc = new DateTimeOffset(2026, 8, 10, 14, 0, 0, TimeSpan.Zero),
        LastLaunchedUtc = null,
        TotalPlaytimeSeconds = 0,
        Launch = InstanceLaunchSettings.Empty,
        Notes = string.Empty,
    };

    [Fact]
    public void RoundTrip_SampleFromArchitectureDoc_PreservesAllFields()
    {
        var original = CreateSample();

        var json = JsonSerializer.Serialize(original, InstanceJsonContext.Default.InstanceMetadata);
        var roundTripped = JsonSerializer.Deserialize(json, InstanceJsonContext.Default.InstanceMetadata);

        roundTripped.ShouldBe(original);
    }

    [Fact]
    public void RoundTrip_WithPlaytimeLastLaunchedNotesAndLaunchSettings_PreservesAllFields()
    {
        var original = CreateSample() with
        {
            LastLaunchedUtc = new DateTimeOffset(2026, 8, 11, 9, 30, 0, TimeSpan.Zero),
            TotalPlaytimeSeconds = 3723,
            Notes = "Monde principal, ne pas supprimer.",
            Launch = new InstanceLaunchSettings
            {
                ExtraArgs = ["--logident", "homestead"],
                Env = new Dictionary<string, string> { ["MESA_GLTHREAD"] = "true" },
            },
        };

        var json = JsonSerializer.Serialize(original, InstanceJsonContext.Default.InstanceMetadata);
        var roundTripped = JsonSerializer.Deserialize(json, InstanceJsonContext.Default.InstanceMetadata);

        roundTripped.ShouldBe(original);
    }

    [Fact]
    public void Serialize_GameVersion_WritesCanonicalString()
    {
        var metadata = CreateSample() with { GameVersion = GameVersion.Parse("1.22.0-rc.10") };

        var json = JsonSerializer.Serialize(metadata, InstanceJsonContext.Default.InstanceMetadata);

        json.ShouldContain("\"gameVersion\":\"1.22.0-rc.10\"");
    }

    [Fact]
    public void Serialize_UsesCamelCasePropertyNames()
    {
        var json = JsonSerializer.Serialize(CreateSample(), InstanceJsonContext.Default.InstanceMetadata);

        json.ShouldContain("\"schemaVersion\":1");
        json.ShouldContain("\"id\":");
        json.ShouldContain("\"name\":");
        json.ShouldContain("\"gameVersion\":");
        json.ShouldContain("\"icon\":");
        json.ShouldContain("\"createdUtc\":");
        json.ShouldContain("\"lastLaunchedUtc\":null");
        json.ShouldContain("\"totalPlaytimeSeconds\":0");
        json.ShouldContain("\"launch\":");
        json.ShouldContain("\"extraArgs\":");
        json.ShouldContain("\"env\":");
        json.ShouldContain("\"notes\":");
    }

    [Fact]
    public void Construct_WithoutOptionalFields_UsesDocumentedDefaults()
    {
        var metadata = new InstanceMetadata
        {
            SchemaVersion = InstanceMetadata.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            Name = "Nouvelle instance",
            GameVersion = GameVersion.Parse("1.21.3"),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        metadata.Icon.ShouldBe("builtin:default");
        metadata.LastLaunchedUtc.ShouldBeNull();
        metadata.TotalPlaytimeSeconds.ShouldBe(0L);
        metadata.Launch.ShouldBe(InstanceLaunchSettings.Empty);
        metadata.Notes.ShouldBe(string.Empty);
    }

    [Fact]
    public void Deserialize_InvalidGameVersion_ThrowsJsonException()
    {
        var json = JsonSerializer.Serialize(CreateSample(), InstanceJsonContext.Default.InstanceMetadata)
            .Replace("1.21.3", "not-a-version", StringComparison.Ordinal);

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(json, InstanceJsonContext.Default.InstanceMetadata));
    }
}