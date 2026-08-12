using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.Json.Nodes;

using Prospect.Core.Auth;
using Prospect.Core.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Auth;

/// <summary>
/// L'injection de la session dans le <c>clientsettings.json</c> du dataPath, seul endroit où le
/// compte sert réellement à quelque chose : c'est le JEU qui lit ce fichier au démarrage pour se
/// considérer connecté, pas le launcher (docs/research/vslauncher-et-distribution.md, section d).
/// Les huit clés et leur orthographe exacte viennent de <c>gameHandlers.ts</c> lignes 73-114.
/// </summary>
public sealed class ClientSettingsSessionWriterTests
{
    private const string DataDirectory = "/data/prospect/instances/homestead/data";

    private static readonly VsSession Session = new()
    {
        Email = "joueuse@example.invalid",
        PlayerName = "Sylve",
        PlayerUid = "3f2b8e14",
        Entitlements = "singleplayer,multiplayer",
        SessionKey = "cle-de-session",
        SessionSignature = "signature-de-session",
        MpToken = "jeton-multijoueur",
        HostGameServer = "true",
    };

    private static (ClientSettingsSessionWriter Writer, MockFileSystem FileSystem) CreateFixture()
    {
        var fileSystem = new MockFileSystem();

        return (new ClientSettingsSessionWriter(fileSystem, new JsonFileStore(fileSystem)), fileSystem);
    }

    private static string SettingsPath(MockFileSystem fileSystem)
        => fileSystem.Path.Combine(DataDirectory, ClientSettingsSessionWriter.FileName);

    private static JsonObject ReadStringSettings(MockFileSystem fileSystem)
    {
        var document = JsonNode.Parse(fileSystem.File.ReadAllText(SettingsPath(fileSystem)))!.AsObject();

        return document["stringSettings"]!.AsObject();
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fileSystem = new MockFileSystem();

        Should.Throw<ArgumentNullException>(() => new ClientSettingsSessionWriter(null!, new JsonFileStore(fileSystem)));
        Should.Throw<ArgumentNullException>(() => new ClientSettingsSessionWriter(fileSystem, null!));
    }

    [Fact]
    public async Task WriteAsync_NoFileYet_CreatesItWithTheEightContractKeys()
    {
        var (writer, fileSystem) = CreateFixture();

        await writer.WriteAsync(DataDirectory, Session, CancellationToken.None);

        var stringSettings = ReadStringSettings(fileSystem);
        stringSettings["mptoken"]!.GetValue<string>().ShouldBe("jeton-multijoueur");
        stringSettings["sessionkey"]!.GetValue<string>().ShouldBe("cle-de-session");
        stringSettings["sessionsignature"]!.GetValue<string>().ShouldBe("signature-de-session");
        stringSettings["useremail"]!.GetValue<string>().ShouldBe("joueuse@example.invalid");
        stringSettings["entitlements"]!.GetValue<string>().ShouldBe("singleplayer,multiplayer");
        stringSettings["playeruid"]!.GetValue<string>().ShouldBe("3f2b8e14");
        stringSettings["playername"]!.GetValue<string>().ShouldBe("Sylve");
        stringSettings["hostgameserver"]!.GetValue<string>().ShouldBe("true");
        stringSettings.Count.ShouldBe(8);
    }

    [Fact]
    public async Task WriteAsync_NoFileYet_CreatesTheDataDirectoryItself()
    {
        var (writer, fileSystem) = CreateFixture();

        await writer.WriteAsync(DataDirectory, Session, CancellationToken.None);

        fileSystem.File.Exists(SettingsPath(fileSystem)).ShouldBeTrue();
    }

    [Fact]
    public async Task WriteAsync_ExistingFile_LeavesEverythingItDoesNotOwnUntouched()
    {
        var (writer, fileSystem) = CreateFixture();
        fileSystem.AddFile(SettingsPath(fileSystem), new MockFileData("""
        {
          "stringSettings": { "playeruid": "ancien-uid", "language": "fr", "serverName": "Chez Sylve" },
          "intSettings": { "guiScale": 3 },
          "boolSettings": { "vsync": true },
          "floatSettings": { "musicLevel": 0.4 }
        }
        """));

        await writer.WriteAsync(DataDirectory, Session, CancellationToken.None);

        var document = JsonNode.Parse(fileSystem.File.ReadAllText(SettingsPath(fileSystem)))!.AsObject();
        document["intSettings"]!["guiScale"]!.GetValue<int>().ShouldBe(3);
        document["boolSettings"]!["vsync"]!.GetValue<bool>().ShouldBeTrue();
        document["floatSettings"]!["musicLevel"]!.GetValue<double>().ShouldBe(0.4, 0.0001);
        var stringSettings = document["stringSettings"]!.AsObject();
        stringSettings["language"]!.GetValue<string>().ShouldBe("fr");
        stringSettings["serverName"]!.GetValue<string>().ShouldBe("Chez Sylve");
        stringSettings["playeruid"]!.GetValue<string>().ShouldBe("3f2b8e14");
    }

    [Fact]
    public async Task WriteAsync_ExistingFileWithoutStringSettings_AddsTheSectionWithoutLosingTheRest()
    {
        var (writer, fileSystem) = CreateFixture();
        fileSystem.AddFile(SettingsPath(fileSystem), new MockFileData("""{ "intSettings": { "guiScale": 2 } }"""));

        await writer.WriteAsync(DataDirectory, Session, CancellationToken.None);

        var document = JsonNode.Parse(fileSystem.File.ReadAllText(SettingsPath(fileSystem)))!.AsObject();
        document["intSettings"]!["guiScale"]!.GetValue<int>().ShouldBe(2);
        document["stringSettings"]!["sessionkey"]!.GetValue<string>().ShouldBe("cle-de-session");
    }

    [Fact]
    public async Task WriteAsync_ExistingFileWhereStringSettingsIsNotAnObject_RebuildsThatSectionOnly()
    {
        var (writer, fileSystem) = CreateFixture();
        fileSystem.AddFile(SettingsPath(fileSystem), new MockFileData("""
        { "stringSettings": "cassé", "intSettings": { "guiScale": 2 } }
        """));

        await writer.WriteAsync(DataDirectory, Session, CancellationToken.None);

        var document = JsonNode.Parse(fileSystem.File.ReadAllText(SettingsPath(fileSystem)))!.AsObject();
        document["intSettings"]!["guiScale"]!.GetValue<int>().ShouldBe(2);
        document["stringSettings"]!.AsObject()["playername"]!.GetValue<string>().ShouldBe("Sylve");
    }

    [Fact]
    public async Task WriteAsync_CorruptedFile_ThrowsWithoutDestroyingWhatIsAlreadyThere()
    {
        var (writer, fileSystem) = CreateFixture();
        fileSystem.AddFile(SettingsPath(fileSystem), new MockFileData("{ pas du JSON"));

        await Should.ThrowAsync<CorruptedFileException>(() => writer.WriteAsync(DataDirectory, Session, CancellationToken.None));

        fileSystem.File.ReadAllText(SettingsPath(fileSystem)).ShouldBe("{ pas du JSON");
    }

    [Fact]
    public async Task WriteAsync_LeavesNoTemporaryFileBehind()
    {
        var (writer, fileSystem) = CreateFixture();

        await writer.WriteAsync(DataDirectory, Session, CancellationToken.None);

        fileSystem.File.Exists(SettingsPath(fileSystem) + JsonFileStore.TempFileSuffix).ShouldBeFalse();
    }

    [Fact]
    public async Task WriteAsync_WritesEveryValueAsAJsonString_BecauseTheGameReadsAStringMap()
    {
        var (writer, fileSystem) = CreateFixture();

        await writer.WriteAsync(DataDirectory, Session, CancellationToken.None);

        // hostgameserver arrive en booléen dans la réponse du service : VS Launcher le recopie tel
        // quel, donc un booléen JSON au milieu d'une section nommée « stringSettings ». Prospect
        // écrit une chaîne, la section reste homogène et le jeu la lit toujours.
        var stringSettings = ReadStringSettings(fileSystem);
        foreach (var (_, value) in stringSettings)
        {
            value!.GetValueKind().ShouldBe(JsonValueKind.String);
        }
    }

    [Fact]
    public async Task WriteAsync_Twice_IsIdempotent()
    {
        var (writer, fileSystem) = CreateFixture();

        await writer.WriteAsync(DataDirectory, Session, CancellationToken.None);
        var first = fileSystem.File.ReadAllText(SettingsPath(fileSystem));
        await writer.WriteAsync(DataDirectory, Session, CancellationToken.None);

        fileSystem.File.ReadAllText(SettingsPath(fileSystem)).ShouldBe(first);
    }

    [Fact]
    public async Task WriteAsync_NullArguments_Throw()
    {
        var (writer, _) = CreateFixture();

        await Should.ThrowAsync<ArgumentException>(() => writer.WriteAsync(string.Empty, Session, CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => writer.WriteAsync(DataDirectory, null!, CancellationToken.None));
    }
}
