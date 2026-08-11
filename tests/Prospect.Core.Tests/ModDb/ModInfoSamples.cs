using System.IO.Compression;
using System.Text;

namespace Prospect.Core.Tests.ModDb;

/// <summary>
/// <c>modinfo.json</c> réels, repris de docs/research/moddb-api.md où ils ont été extraits de zips
/// téléchargés pour de bon, plus quelques variantes fabriquées pour exercer les tolérances que la
/// recherche annonce (casse des clés, commentaires, virgules terminales).
/// </summary>
internal static class ModInfoSamples
{
    /// <summary>Config lib : dépendance unique, <c>side</c> universel, indentation à 4 espaces.</summary>
    public const string ConfigLib = """
    {
        "type": "code",
        "name": "Config lib",
        "modid": "configlib",
        "version": "1.12.0",
        "description": "A universal place to configure your mods. Makes it possible for content mods to have configs too.",
        "authors": [ "Maltiez", "The Insanity God" ],
        "dependencies": {
            "vsimgui": "1.2.0"
        },
        "side" : "universal",
        "requiredOnClient": true,
        "requiredOnServer": false
    }
    """;

    /// <summary>JSON Patches lib : bloc <c>dependencies</c> vide, indentation à 2 espaces.</summary>
    public const string JsonPatchesLib = """
    {
      "type": "code",
      "name": "JSON Patches lib",
      "modid": "jsonpatcheslib",
      "version": "1.5.6",
      "description": "Implements more convenient syntax and more functionality for patching JSON assets.",
      "authors": [ "Maltiez" ],
      "dependencies": {

      },
      "side" : "universal",
      "requiredOnClient": true,
      "requiredOnServer": true
    }
    """;

    /// <summary>Extra Info : seul des trois à déclarer <c>contributors</c> et une dépendance sur <c>game</c>.</summary>
    public const string ExtraInfo = """
    {
        "type": "code",
        "side": "client",
        "modid": "extrainfo",
        "name": "Extra Info",
        "description": "Useful information for handbook, blocks, items and entities",
        "authors": ["Craluminum2413 (Dana)"],
        "contributors": ["Novocain"],
        "version": "2.2.1",
        "dependencies": {
            "game": "1.22.0"
        }
    }
    """;

    /// <summary>Casse des clés à la sauce PascalCase, telle qu'on la rencontre dans la nature.</summary>
    public const string PascalCaseKeys = """
    {
        "Type": "Content",
        "Name": "Carry Capacity",
        "ModID": "carrycapacity",
        "Version": "1.8.0",
        "Authors": [ "copygirl" ],
        "Side": "Universal",
        "Dependencies": { "game": "1.20.0" }
    }
    """;

    /// <summary>Commentaires et virgule terminale : refusés par un parseur strict, acceptés par le jeu.</summary>
    public const string WithCommentsAndTrailingComma = """
    {
        // le nom affiché dans la liste des mods
        "name": "Commented Mod",
        "modid": "commentedmod",
        "version": "0.3.1", /* version courante */
        "authors": ["Someone"],
        "dependencies": {
            "configlib": "1.0.0",
        },
    }
    """;

    /// <summary>Aucun <c>modid</c> : le jeu le dérive du nom, nous aussi.</summary>
    public const string WithoutModId = """
    {
        "name": "My Great Mod!",
        "version": "1.0.0"
    }
    """;

    /// <summary>Dépendances aux formes joker et à une valeur illisible.</summary>
    public const string WithLooseDependencies = """
    {
        "name": "Loose",
        "modid": "loose",
        "version": "1.0.0",
        "dependencies": {
            "anyversion": "",
            "star": "*",
            "broken": "1.20.*",
            "survival": "",
            "creative": ""
        }
    }
    """;

    /// <summary>JSON franchement cassé : accolade jamais fermée.</summary>
    public const string Malformed = """{ "name": "Cassé", "modid": """;

    /// <summary>Construit une archive de mod en mémoire, avec les entrées demandées.</summary>
    public static byte[] BuildArchive(string? modInfoJson, byte[]? icon = null, string modInfoEntryName = "modinfo.json", string iconEntryName = "modicon.png")
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (modInfoJson is not null)
            {
                var entry = archive.CreateEntry(modInfoEntryName);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(modInfoJson));
            }

            if (icon is not null)
            {
                var entry = archive.CreateEntry(iconEntryName);
                using var stream = entry.Open();
                stream.Write(icon);
            }

            var assets = archive.CreateEntry("assets/placeholder.json");
            using var assetStream = assets.Open();
            assetStream.Write("{}"u8);
        }

        return buffer.ToArray();
    }
}