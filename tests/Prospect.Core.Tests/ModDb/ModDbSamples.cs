namespace Prospect.Core.Tests.ModDb;

/// <summary>
/// Échantillons JSON repris tels quels de docs/research/moddb-api.md, où chacun provient d'un
/// appel réel daté du 2026-08-10. Ils sont tronqués (un mod au lieu de 7 994, trois releases au
/// lieu de 85) mais jamais réécrits : les valeurs, la casse des clés et les types restent ceux du
/// serveur, y compris ses incohérences.
/// </summary>
internal static class ModDbSamples
{
    /// <summary>
    /// <c>GET /api/mods</c>. Contient volontairement les trois cas piégeux relevés par la
    /// recherche : un mod complet, un mod à <c>logo</c> et <c>urlalias</c> nuls avec
    /// <c>tags: [""]</c>, et un outil externe sans aucun <c>modidstrs</c>.
    /// </summary>
    public const string ModList = """
    {
      "statuscode": "200",
      "mods": [
        {
          "modid": 792,
          "assetid": 3829,
          "downloads": 1095320,
          "follows": 8133,
          "trendingpoints": 0,
          "comments": 1440,
          "name": "BetterRuins",
          "summary": "Adds many new ruins over and underground to your survival game world.",
          "modidstrs": ["betterruins"],
          "author": "NiclAss",
          "urlalias": "betterruins",
          "side": "both",
          "type": "mod",
          "logo": "https://moddbcdn.vintagestory.at/BetterRuinsmoddbicon_021a6a8838be7058c2ab618ce0097224_480_320.png",
          "tags": ["Crafting", "Exploration", "Lore", "Story", "Structures", "Travel & Exploration", "Worldgen"],
          "lastreleased": "2026-07-28 18:59:32"
        },
        {
          "modid": 1783,
          "assetid": 9551,
          "downloads": 627953,
          "follows": 0,
          "trendingpoints": 12,
          "comments": 0,
          "name": "Config lib",
          "summary": "A universal place to configure your mods.",
          "modidstrs": ["configlib"],
          "author": "Maltiez",
          "urlalias": null,
          "side": "both",
          "type": "mod",
          "logo": null,
          "tags": [""],
          "lastreleased": "2026-05-01 12:03:34"
        },
        {
          "modid": 4021,
          "assetid": 12004,
          "downloads": 812,
          "follows": 3,
          "trendingpoints": 0,
          "comments": 0,
          "name": "VS Model Creator",
          "summary": "Outil externe d'édition de modèles.",
          "modidstrs": [],
          "author": "Tyron",
          "urlalias": null,
          "side": "client",
          "type": "externaltool",
          "logo": null,
          "tags": [],
          "lastreleased": null
        }
      ]
    }
    """;

    /// <summary>
    /// <c>GET /api/mod/1783</c> (Config lib), tronqué à trois releases. La release 1.9.0 est
    /// volontairement taguée sur 1.21.x seulement, pour exercer le choix par tag exact.
    /// </summary>
    public const string ModDetail = """
    {
      "statuscode": "200",
      "mod": {
        "modid": 1783,
        "assetid": 9551,
        "name": "Config lib",
        "text": "<h2>Config lib</h2><p>A universal place to configure your mods.</p><p>See the <a href=\"https://example.invalid\">docs</a>.</p>",
        "author": "Maltiez",
        "urlalias": null,
        "logofilename": "https://moddbcdn.vintagestory.at/configlib_icon.png",
        "logofile": "https://moddbcdn.vintagestory.at/configlib_icon.png",
        "logofiledb": null,
        "homepageurl": "",
        "sourcecodeurl": "",
        "trailervideourl": "",
        "issuetrackerurl": "",
        "wikiurl": "",
        "downloads": 627953,
        "follows": 0,
        "trendingpoints": 0,
        "comments": 0,
        "side": "both",
        "type": "mod",
        "created": "2023-01-02 10:00:00",
        "lastreleased": "2026-05-01 12:03:34",
        "lastmodified": "2026-05-01 12:03:34",
        "tags": ["Utility"],
        "releases": [
          {
            "releaseid": 39980,
            "fileid": 88961,
            "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.12.0_abc.zip?dl=configlib_1.12.0.zip",
            "filename": "configlib_1.12.0.zip",
            "downloads": 118728,
            "tags": ["1.22.0-pre.1", "1.22.0-rc.1", "1.22.0", "1.22.1"],
            "modidstr": "configlib",
            "modversion": "1.12.0",
            "changelog": "<p>Compat 1.22</p>",
            "created": "2026-05-01 12:03:34"
          },
          {
            "releaseid": 38314,
            "fileid": 84120,
            "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.11.1_def.zip?dl=configlib_1.11.1.zip",
            "filename": "configlib_1.11.1.zip",
            "downloads": 90210,
            "tags": ["1.21.3", "1.22.0"],
            "modidstr": "configlib",
            "modversion": "1.11.1",
            "changelog": null,
            "created": "2026-02-11 09:22:10"
          },
          {
            "releaseid": 31002,
            "fileid": 70311,
            "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.9.0_ghi.zip?dl=configlib_1.9.0.zip",
            "filename": "configlib_1.9.0.zip",
            "downloads": 41007,
            "tags": ["1.21.0", "1.21.3"],
            "modidstr": "configlib",
            "modversion": "1.9.0",
            "changelog": null,
            "created": "2025-11-04 18:00:00"
          }
        ]
      }
    }
    """;

    /// <summary>Mod inconnu : le seul indicateur d'échec est ce champ, le vrai code HTTP reste 200.</summary>
    public const string NotFound = """{"statuscode":"404"}""";

    /// <summary>Endpoint retiré (<c>/api/changelogs</c>) : même piège, autre code.</summary>
    public const string Gone = """
    {"reason":"This information was previously available, but is no longer distributed.","statuscode":"410"}
    """;

    /// <summary><c>GET /api/tags</c> : noter le <c>tagid</c> en CHAÎNE, contrairement à /api/gameversions.</summary>
    public const string TagList = """
    {
      "statuscode": "200",
      "tags": [
        {"tagid": "467", "name": "Absolute Cinema", "color": "#92C96AFF"},
        {"tagid": "285", "name": "Accessibility", "color": "#92C96AFF"},
        {"tagid": "12", "name": "Utility", "color": "#92C96AFF"},
        {"tagid": "", "name": "", "color": "#92C96AFF"}
      ]
    }
    """;

    /// <summary><c>GET /api/v2/mods/1783/releases/latest</c> : timestamp Unix et chemin relatif.</summary>
    public const string V2LatestRelease = """
    {
      "releaseId": 39980,
      "identifier": "configlib",
      "version": "1.12.0",
      "compatibleGameVersions": ["1.22.1", "1.22.0", "1.22.0-rc.10", "1.22.0-pre.1"],
      "created": 1777637014,
      "fileName": "configlib_1.12.0.zip",
      "fileUrl": "/download/88961/configlib_1.12.0.zip"
    }
    """;

    /// <summary><c>GET /api/v2/mods/install-information?ids=configlib@1.0.0&amp;gv=1.22.0</c>, forme observée en direct.</summary>
    public const string V2InstallInformation = """
    {
      "data": {
        "configlib": {
          "recommendedUpgrade": "1.12.0",
          "fileName": "configlib_1.0.0.zip",
          "fileUrl": "/download/19456/configlib_1.0.0.zip"
        }
      }
    }
    """;

    /// <summary>
    /// Même endpoint avec <c>resolve-deps=true</c> et un mod à dépendances non triviales. Les clés
    /// <c>resolved</c>/<c>warnings</c> sont documentées dans <c>lib/relations.php</c> mais la
    /// recherche n'en a pas obtenu d'échantillon live : cette forme reste une hypothèse, d'où la
    /// vérification locale qui ne dépend jamais d'elle.
    /// </summary>
    public const string V2InstallInformationWithDependencies = """
    {
      "data": {
        "configlib": {
          "recommendedUpgrade": "1.12.0",
          "fileName": "configlib_1.12.0.zip",
          "fileUrl": "/download/88961/configlib_1.12.0.zip"
        }
      },
      "resolved": {
        "vsimgui": {
          "version": "1.2.0",
          "fileName": "vsimgui_1.2.0.zip",
          "fileUrl": "/download/70001/vsimgui_1.2.0.zip"
        }
      },
      "warnings": ["vsimgui n'a pas de release taguée 1.22.0"]
    }
    """;
}