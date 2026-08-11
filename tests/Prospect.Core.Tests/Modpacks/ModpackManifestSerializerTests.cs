using System.Text;

using Prospect.Core.Common;
using Prospect.Core.Modpacks;

using Shouldly;

namespace Prospect.Core.Tests.Modpacks;

public sealed class ModpackManifestSerializerTests
{
    private static ModpackManifest SampleManifest() => new()
    {
        SchemaVersion = ModpackManifest.CurrentSchemaVersion,
        Name = "Pack exemple",
        Author = "Pixnop",
        GameVersion = GameVersion.Parse("1.21.3"),
        Mods =
        [
            new ModpackManifestMod
            {
                ModId = "carrycapacity",
                Version = ModVersion.Parse("1.8.0"),
                FileId = 12345,
                Sha256 = "a3f5c1",
            },
            new ModpackManifestMod
            {
                ModId = "configlib",
                Version = ModVersion.Parse("1.12.0"),
                Enabled = false,
            },
        ],
    };

    private static async Task<string> WriteToStringAsync(ModpackManifest manifest)
    {
        using var stream = new MemoryStream();
        await ModpackManifestSerializer.WriteAsync(stream, manifest, CancellationToken.None);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static Task<ModpackManifest> ReadFromStringAsync(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        return ModpackManifestSerializer.ReadAsync(stream, CancellationToken.None);
    }

    // ── Round-trip ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteThenRead_RoundTripsEveryField()
    {
        var original = SampleManifest();

        var json = await WriteToStringAsync(original);
        var read = await ReadFromStringAsync(json);

        read.SchemaVersion.ShouldBe(original.SchemaVersion);
        read.Name.ShouldBe(original.Name);
        read.Author.ShouldBe(original.Author);
        read.GameVersion.ShouldBe(original.GameVersion);
        read.Mods.Count.ShouldBe(2);

        read.Mods[0].ModId.ShouldBe("carrycapacity");
        read.Mods[0].Version.ShouldBe(ModVersion.Parse("1.8.0"));
        read.Mods[0].FileId.ShouldBe(12345);
        read.Mods[0].Sha256.ShouldBe("a3f5c1");
        read.Mods[0].IsEnabled.ShouldBeTrue();

        read.Mods[1].ModId.ShouldBe("configlib");
        read.Mods[1].FileId.ShouldBeNull();
        read.Mods[1].Sha256.ShouldBeNull();
        read.Mods[1].IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Write_MatchesTheDocumentedContractShape()
    {
        var manifest = new ModpackManifest
        {
            SchemaVersion = 1,
            Name = "Pack exemple",
            Author = "Pixnop",
            GameVersion = GameVersion.Parse("1.21.3"),
            Mods = [new ModpackManifestMod { ModId = "carrycapacity", Version = ModVersion.Parse("1.8.0"), FileId = 12345 }],
        };

        var json = await WriteToStringAsync(manifest);

        json.ShouldContain("\"schemaVersion\": 1");
        json.ShouldContain("\"gameVersion\": \"1.21.3\"");
        json.ShouldContain("\"modId\": \"carrycapacity\"");
        json.ShouldContain("\"fileId\": 12345");
    }

    // ── Champ enabled : absent quand true ──────────────────────────────────────────

    [Fact]
    public async Task Write_EnabledMod_OmitsTheEnabledField()
    {
        var manifest = SampleManifest() with
        {
            Mods = [new ModpackManifestMod { ModId = "carrycapacity", Version = ModVersion.Parse("1.8.0") }],
        };

        var json = await WriteToStringAsync(manifest);

        json.ShouldNotContain("enabled");
    }

    [Fact]
    public async Task Write_DisabledMod_WritesEnabledFalse()
    {
        var manifest = SampleManifest() with
        {
            Mods = [new ModpackManifestMod { ModId = "carrycapacity", Version = ModVersion.Parse("1.8.0"), Enabled = false }],
        };

        var json = await WriteToStringAsync(manifest);

        json.ShouldContain("\"enabled\": false");
    }

    [Fact]
    public async Task Read_MissingEnabledField_DefaultsToTrue()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "name": "Pack exemple",
          "gameVersion": "1.21.3",
          "mods": [ { "modId": "carrycapacity", "version": "1.8.0" } ]
        }
        """;

        var manifest = await ReadFromStringAsync(json);

        manifest.Mods.ShouldHaveSingleItem().IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Read_ExplicitEnabledTrue_IsAlsoAccepted()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "name": "Pack exemple",
          "gameVersion": "1.21.3",
          "mods": [ { "modId": "carrycapacity", "version": "1.8.0", "enabled": true } ]
        }
        """;

        var manifest = await ReadFromStringAsync(json);

        manifest.Mods.ShouldHaveSingleItem().IsEnabled.ShouldBeTrue();
    }

    // ── Tolérance aux champs inconnus (schémas futurs) ─────────────────────────────

    [Fact]
    public async Task Read_UnknownTopLevelAndModFields_AreIgnoredRatherThanRejected()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "name": "Pack exemple",
          "gameVersion": "1.21.3",
          "futureFeature": { "nested": true },
          "mods": [ { "modId": "carrycapacity", "version": "1.8.0", "futureModField": 42 } ]
        }
        """;

        var manifest = await ReadFromStringAsync(json);

        manifest.Name.ShouldBe("Pack exemple");
        manifest.Mods.ShouldHaveSingleItem().ModId.ShouldBe("carrycapacity");
    }

    [Fact]
    public async Task Read_NoModsArray_DefaultsToEmpty()
    {
        const string json = """
        { "schemaVersion": 1, "name": "Juste une version de jeu", "gameVersion": "1.21.3" }
        """;

        var manifest = await ReadFromStringAsync(json);

        manifest.Mods.ShouldBeEmpty();
    }

    // ── Optionnels ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Read_NoAuthor_IsNull()
    {
        const string json = """
        { "schemaVersion": 1, "name": "Sans auteur", "gameVersion": "1.21.3" }
        """;

        var manifest = await ReadFromStringAsync(json);

        manifest.Author.ShouldBeNull();
    }

    // ── Validation avec erreurs claires ────────────────────────────────────────────

    [Fact]
    public async Task Read_MalformedJson_ThrowsWithAClearMessage()
    {
        var exception = await Should.ThrowAsync<ModpackManifestInvalidException>(
            () => ReadFromStringAsync("{ not json"));

        exception.Message.ShouldNotBeNullOrWhiteSpace();
        exception.InnerException.ShouldNotBeNull();
    }

    [Fact]
    public async Task Read_LiteralNull_ThrowsRatherThanReturningNull()
        => await Should.ThrowAsync<ModpackManifestInvalidException>(() => ReadFromStringAsync("null"));

    [Fact]
    public async Task Read_FutureSchemaVersion_IsRejectedRatherThanMisread()
    {
        const string json = """
        { "schemaVersion": 2, "name": "Pack futur", "gameVersion": "1.21.3" }
        """;

        var exception = await Should.ThrowAsync<ModpackManifestInvalidException>(() => ReadFromStringAsync(json));

        exception.Message.ShouldContain("v2");
    }

    [Fact]
    public async Task Read_ZeroSchemaVersion_IsRejected()
    {
        const string json = """
        { "schemaVersion": 0, "name": "Pack invalide", "gameVersion": "1.21.3" }
        """;

        await Should.ThrowAsync<ModpackManifestInvalidException>(() => ReadFromStringAsync(json));
    }

    [Fact]
    public async Task Read_BlankName_IsRejected()
    {
        const string json = """
        { "schemaVersion": 1, "name": "   ", "gameVersion": "1.21.3" }
        """;

        await Should.ThrowAsync<ModpackManifestInvalidException>(() => ReadFromStringAsync(json));
    }

    [Fact]
    public async Task Read_MissingName_IsRejected()
    {
        const string json = """
        { "schemaVersion": 1, "gameVersion": "1.21.3" }
        """;

        await Should.ThrowAsync<ModpackManifestInvalidException>(() => ReadFromStringAsync(json));
    }

    [Fact]
    public async Task Read_MissingGameVersion_IsRejected()
    {
        const string json = """
        { "schemaVersion": 1, "name": "Pack sans version" }
        """;

        await Should.ThrowAsync<ModpackManifestInvalidException>(() => ReadFromStringAsync(json));
    }

    [Fact]
    public async Task Read_UnparseableGameVersion_IsRejectedWithAClearMessage()
    {
        const string json = """
        { "schemaVersion": 1, "name": "Pack cassé", "gameVersion": "pas-une-version" }
        """;

        var exception = await Should.ThrowAsync<ModpackManifestInvalidException>(() => ReadFromStringAsync(json));

        exception.Message.ShouldContain("pas-une-version");
    }

    [Fact]
    public async Task Read_ModWithoutModId_IsRejected()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "name": "Pack cassé",
          "gameVersion": "1.21.3",
          "mods": [ { "version": "1.0.0" } ]
        }
        """;

        await Should.ThrowAsync<ModpackManifestInvalidException>(() => ReadFromStringAsync(json));
    }

    [Fact]
    public async Task Read_ModWithoutVersion_IsRejected()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "name": "Pack cassé",
          "gameVersion": "1.21.3",
          "mods": [ { "modId": "carrycapacity" } ]
        }
        """;

        await Should.ThrowAsync<ModpackManifestInvalidException>(() => ReadFromStringAsync(json));
    }
}