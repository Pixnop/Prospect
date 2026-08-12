using Prospect.Core.Migration;
using Prospect.Core.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Migration;

/// <summary>
/// Fixtures fidèles à la forme réelle de <c>config.json</c> (<c>ConfigType</c>/<c>InstallationType</c>/
/// <c>GameVersionType</c>, <c>global.d.ts</c> du dépôt VS Launcher), pas des simplifications :
/// mêmes noms de champs, mêmes types, mêmes valeurs par défaut que <c>defaultInstallation</c> côté
/// VSL.
/// </summary>
public class VslConfigParserTests
{
    private const string SourcePath = "/home/pixnop/.config/VSLauncher/config.json";

    [Fact]
    public void Parse_RealisticDocument_ReturnsAllInstallationsAndGameVersions()
    {
        const string json = """
        {
          "version": 1.6,
          "lastUsedInstallation": "a1b2c3",
          "defaultInstallationsFolder": "/home/pixnop/.config/VSLInstallations",
          "defaultVersionsFolder": "/home/pixnop/.config/VSLGameVersions",
          "backupsFolder": "/home/pixnop/.config/VSLBackups",
          "window": { "width": 1280, "height": 720, "x": 0, "y": 0, "maximized": false },
          "account": null,
          "installations": [
            {
              "id": "a1b2c3",
              "name": "Survie médiévale",
              "icon": "default",
              "path": "/home/pixnop/.config/VSLInstallations/survie-medievale",
              "version": "1.20.4",
              "startParams": "-logexcept -tracelog",
              "backupsLimit": 3,
              "backupsAuto": false,
              "compressionLevel": 4,
              "backups": [],
              "lastTimePlayed": 1770000000000,
              "totalTimePlayed": 3600000,
              "mesaGlThread": true,
              "envVars": "DXVK_HUD=fps"
            },
            {
              "id": "d4e5f6",
              "name": "Bac à sable créatif",
              "icon": "default",
              "path": "/home/pixnop/.config/VSLInstallations/bac-a-sable",
              "version": "1.21.3",
              "startParams": "",
              "backupsLimit": 3,
              "backupsAuto": true,
              "compressionLevel": 4,
              "backups": [],
              "lastTimePlayed": -1,
              "totalTimePlayed": 0,
              "mesaGlThread": false,
              "envVars": ""
            }
          ],
          "gameVersions": [
            { "version": "1.20.4", "path": "/home/pixnop/.config/VSLGameVersions/1.20.4" },
            { "version": "1.21.3", "path": "/home/pixnop/.config/VSLGameVersions/1.21.3" }
          ],
          "favMods": [12345],
          "customIcons": []
        }
        """;

        var result = VslConfigParser.Parse(json, SourcePath);

        result.Installations.Count.ShouldBe(2);
        result.GameVersions.Count.ShouldBe(2);
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_Installation_ReadsEveryFieldVerbatim()
    {
        const string json = """
        {
          "installations": [
            {
              "id": "a1b2c3",
              "name": "Survie médiévale",
              "path": "/home/pixnop/.config/VSLInstallations/survie-medievale",
              "version": "1.20.4",
              "startParams": "-logexcept -tracelog",
              "lastTimePlayed": 1770000000000,
              "totalTimePlayed": 3600000,
              "mesaGlThread": true,
              "envVars": "DXVK_HUD=fps"
            }
          ],
          "gameVersions": []
        }
        """;

        var installation = VslConfigParser.Parse(json, SourcePath).Installations.ShouldHaveSingleItem();

        installation.Id.ShouldBe("a1b2c3");
        installation.Name.ShouldBe("Survie médiévale");
        installation.Path.ShouldBe("/home/pixnop/.config/VSLInstallations/survie-medievale");
        installation.Version.ShouldBe("1.20.4");
        installation.StartParams.ShouldBe("-logexcept -tracelog");
        installation.LastTimePlayedMs.ShouldBe(1770000000000L);
        installation.TotalTimePlayedMs.ShouldBe(3600000L);
        installation.MesaGlThread.ShouldBeTrue();
        installation.EnvVars.ShouldBe("DXVK_HUD=fps");
    }

    [Fact]
    public void Parse_InstallationWithStartParamsHoldingSeveralFlags_KeepsRawStringUnsplit()
    {
        // Le parser ne tokenise pas lui-même (voir VslStartParamsTokenizer, une étape séparée) :
        // il doit rendre la chaîne telle quelle, plusieurs indicateurs compris.
        const string json = """
        {
          "installations": [
            { "id": "x", "name": "Test", "path": "/data/x", "version": "1.20.4",
              "startParams": "-logexcept -tracelog -dataPath=/tmp/override", "envVars": "" }
          ],
          "gameVersions": []
        }
        """;

        var installation = VslConfigParser.Parse(json, SourcePath).Installations.ShouldHaveSingleItem();

        installation.StartParams.ShouldBe("-logexcept -tracelog -dataPath=/tmp/override");
    }

    [Fact]
    public void Parse_MissingOptionalFields_FallBackToVslDefaults()
    {
        // ensureConfigProperties côté VSL : name/startParams/envVars vides, lastTimePlayed = -1,
        // totalTimePlayed = 0, mesaGlThread = false. Seul "path" est vraiment requis ici.
        const string json = """
        {
          "installations": [ { "path": "/data/minimal" } ],
          "gameVersions": []
        }
        """;

        var installation = VslConfigParser.Parse(json, SourcePath).Installations.ShouldHaveSingleItem();

        installation.Id.ShouldBe(string.Empty);
        installation.Name.ShouldBe(string.Empty);
        installation.Version.ShouldBe(string.Empty);
        installation.StartParams.ShouldBe(string.Empty);
        installation.EnvVars.ShouldBe(string.Empty);
        installation.MesaGlThread.ShouldBeFalse();
        installation.LastTimePlayedMs.ShouldBe(-1L);
        installation.TotalTimePlayedMs.ShouldBe(0L);
    }

    [Fact]
    public void Parse_InstallationMissingPath_IsReportedAsIssueAndExcluded()
    {
        const string json = """
        {
          "installations": [ { "id": "a", "name": "Sans chemin", "version": "1.20.4" } ],
          "gameVersions": []
        }
        """;

        var result = VslConfigParser.Parse(json, SourcePath);

        result.Installations.ShouldBeEmpty();
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.EntryLabel.ShouldBe("Sans chemin");
        issue.Reason.ShouldBe("chemin manquant");
    }

    [Fact]
    public void Parse_InstallationWithBlankPath_IsReportedAsIssueAndExcluded()
    {
        const string json = """
        {
          "installations": [ { "id": "a", "name": "Chemin vide", "path": "   " } ],
          "gameVersions": []
        }
        """;

        var result = VslConfigParser.Parse(json, SourcePath);

        result.Installations.ShouldBeEmpty();
        result.Issues.ShouldHaveSingleItem();
    }

    [Fact]
    public void Parse_OneCorruptedInstallationEntry_DoesNotBlockTheOthers()
    {
        // "entrée corrompue tolérée" : ici, le champ path a le mauvais type JSON (nombre au lieu de
        // chaîne) sur l'entrée du milieu, ce qui la rend inexploitable, mais les deux autres,
        // valides, doivent rester dans le résultat.
        const string json = """
        {
          "installations": [
            { "id": "a", "name": "Bonne 1", "path": "/data/a", "version": "1.20.4" },
            { "id": "b", "name": "Corrompue", "path": 12345, "version": "1.20.4" },
            { "id": "c", "name": "Bonne 2", "path": "/data/c", "version": "1.21.3" }
          ],
          "gameVersions": []
        }
        """;

        var result = VslConfigParser.Parse(json, SourcePath);

        result.Installations.Count.ShouldBe(2);
        result.Installations.ShouldContain(i => i.Name == "Bonne 1");
        result.Installations.ShouldContain(i => i.Name == "Bonne 2");
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.EntryLabel.ShouldBe("installations[1]");
        issue.Reason.ShouldBe("entrée illisible");
    }

    [Fact]
    public void Parse_InstallationEntryThatIsNotAnObject_IsReportedAsIssue()
    {
        const string json = """
        {
          "installations": [ "not-an-object" ],
          "gameVersions": []
        }
        """;

        var result = VslConfigParser.Parse(json, SourcePath);

        result.Installations.ShouldBeEmpty();
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.EntryLabel.ShouldBe("installations[0]");
        issue.Reason.ShouldBe("entrée illisible");
    }

    [Fact]
    public void Parse_MissingInstallationsArray_ReturnsEmptyListWithoutError()
    {
        const string json = "{ \"gameVersions\": [] }";

        var result = VslConfigParser.Parse(json, SourcePath);

        result.Installations.ShouldBeEmpty();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_GameVersion_ReadsVersionAndPath()
    {
        const string json = """
        {
          "installations": [],
          "gameVersions": [ { "version": "1.20.4", "path": "/home/pixnop/.config/VSLGameVersions/1.20.4" } ]
        }
        """;

        var entry = VslConfigParser.Parse(json, SourcePath).GameVersions.ShouldHaveSingleItem();

        entry.Version.ShouldBe("1.20.4");
        entry.Path.ShouldBe("/home/pixnop/.config/VSLGameVersions/1.20.4");
    }

    [Fact]
    public void Parse_GameVersionMissingPath_IsReportedAsIssueAndExcluded()
    {
        const string json = """
        {
          "installations": [],
          "gameVersions": [ { "version": "1.20.4" } ]
        }
        """;

        var result = VslConfigParser.Parse(json, SourcePath);

        result.GameVersions.ShouldBeEmpty();
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.EntryLabel.ShouldBe("1.20.4");
        issue.Reason.ShouldBe("chemin ou version manquant");
    }

    [Fact]
    public void Parse_GameVersionMissingVersion_IsReportedAsIssueAndExcluded()
    {
        const string json = """
        {
          "installations": [],
          "gameVersions": [ { "path": "/data/engine" } ]
        }
        """;

        var result = VslConfigParser.Parse(json, SourcePath);

        result.GameVersions.ShouldBeEmpty();
        result.Issues.ShouldHaveSingleItem();
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsCorruptedFileExceptionWithSourcePath()
    {
        const string json = "{ this is not valid json";

        var exception = Should.Throw<CorruptedFileException>(() => VslConfigParser.Parse(json, SourcePath));

        exception.Path.ShouldBe(SourcePath);
    }

    [Fact]
    public void Parse_RootIsNotAnObject_ThrowsCorruptedFileException()
    {
        const string json = "[1, 2, 3]";

        Should.Throw<CorruptedFileException>(() => VslConfigParser.Parse(json, SourcePath));
    }

    [Fact]
    public void Parse_EmptyString_ThrowsCorruptedFileException()
    {
        Should.Throw<CorruptedFileException>(() => VslConfigParser.Parse(string.Empty, SourcePath));
    }

    [Fact]
    public void Parse_NullJson_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => VslConfigParser.Parse(null!, SourcePath));
    }
}