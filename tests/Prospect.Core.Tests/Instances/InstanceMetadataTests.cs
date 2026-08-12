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

    [Fact]
    public void Deserialize_JsonMissingOptionalFields_LeavesThemNull()
    {
        // Caractérise le piège avant tout correctif applicatif (voir InstanceMetadata.Normalized()) :
        // ce type porte des membres required (SchemaVersion/Id/Name/GameVersion), donc
        // System.Text.Json construit l'objet sans passer par son constructeur implicite et ne
        // rejoue jamais les initialiseurs `= DefaultIcon`/`= InstanceLaunchSettings.Empty`/
        // `= string.Empty` quand le champ JSON correspondant est absent. Un instance.json partiel
        // (édité à la main, ou écrit par un schéma antérieur) désérialise donc ces trois champs à
        // null malgré leurs défauts documentés. FileSystemInstanceRepository.LoadAsync est le seul
        // rempart (voir son test dédié) : ce test-ci fixe le comportement brut de la désérialisation
        // elle-même, qui reste inchangé par construction (Normalized() n'est pas appelé ici).
        var json = """
        {
          "schemaVersion": 1,
          "id": "0c9c1f57-8b2e-4f2a-9c41-3d8a12f7b6e0",
          "name": "Homestead 1.21",
          "gameVersion": "1.21.3",
          "createdUtc": "2026-08-10T14:00:00+00:00"
        }
        """;

        var metadata = JsonSerializer.Deserialize(json, InstanceJsonContext.Default.InstanceMetadata);

        metadata.ShouldNotBeNull();
        metadata.Icon.ShouldBeNull();
        metadata.Launch.ShouldBeNull();
        metadata.Notes.ShouldBeNull();
    }

    [Fact]
    public void Normalized_NullReferenceFields_RestoresDocumentedDefaults()
    {
        // null! parce que le compilateur refuserait autrement cette construction (Icon/Launch/Notes
        // sont non-nullables par contrat) : exactement le contrat qu'un instance.json partiel viole
        // silencieusement via System.Text.Json (voir le test ci-dessus), simulé ici sans dépendre du
        // détail de sérialisation pour tester Normalized() isolément.
        var metadata = CreateSample() with { Icon = null!, Launch = null!, Notes = null! };

        var normalized = metadata.Normalized();

        normalized.Icon.ShouldBe(InstanceMetadata.DefaultIcon);
        normalized.Launch.ShouldBe(InstanceLaunchSettings.Empty);
        normalized.Notes.ShouldBe(string.Empty);
    }

    [Fact]
    public void Normalized_ValuesAlreadySet_LeavesThemUnchanged()
    {
        var metadata = CreateSample() with { Icon = "file:custom.png", Notes = "Une note." };

        var normalized = metadata.Normalized();

        normalized.Icon.ShouldBe("file:custom.png");
        normalized.Notes.ShouldBe("Une note.");
    }
}