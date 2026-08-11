using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Tests.Http;
using Prospect.Core.Tests.ModDb;

namespace Prospect.Core.Tests.Modpacks;

/// <summary>
/// Écosystème factice partagé par les tests d'import et de round-trip de modpacks : un ModDB avec
/// deux mods réels (configlib, vsimgui) et une version de jeu téléchargeable, tous servis par le
/// MÊME répondeur HTTP factice, plus le catalogue de versions qui va avec. Aucun appel réseau réel
/// ne peut sortir d'ici (docs/architecture.md, « TDD réel, réseau simulé »).
/// </summary>
[SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "Calcule l'empreinte attendue par les tests, en miroir du condensat que publie le catalogue officiel.")]
internal sealed class ModpackTestServer
{
    public static readonly byte[] ConfigLibArchive = ModInfoSamples.BuildArchive(ModInfoSamples.ConfigLib);

    public static readonly byte[] VsImGuiArchive = ModInfoSamples.BuildArchive("""
    { "type": "code", "name": "VS ImGui", "modid": "vsimgui", "version": "1.3.0", "authors": ["Maltiez"] }
    """);

    public static readonly GameVersion GameVersion = GameVersion.Parse("1.21.3");
    public static readonly Uri GameVersionCdnUrl = new("https://cdn.example/vs_1.21.3.tar.gz");

    private static readonly byte[] GameVersionArchive = "vintagestory-fake-archive"u8.ToArray();

    /// <summary>Empreinte MD5 réelle de l'archive de version de jeu factice, telle que le catalogue l'annoncerait.</summary>
    public static readonly string GameVersionMd5 = Convert.ToHexStringLower(MD5.HashData(GameVersionArchive));

    /// <summary>Faux pour simuler un modId retiré/inconnu du ModDB (statuscode 404).</summary>
    public bool ConfigLibKnown { get; set; } = true;

    /// <summary>Vrai pour simuler un ModDB entièrement injoignable.</summary>
    public bool ModDbOffline { get; set; }

    /// <summary>Vrai pour livrer moins d'octets que le <c>HEAD</c> n'en annonce sur les zips de mods.</summary>
    public bool TruncateModDownloads { get; set; }

    /// <summary>Catalogue de versions du jeu ne contenant que <see cref="GameVersion"/>, prêt pour <c>FakeGameVersionCatalog</c>.</summary>
    public static GameVersionCatalog BuildGameCatalog()
    {
        var asset = new GameVersionAsset(
            GamePlatforms.Linux,
            "vs_1.21.3.tar.gz",
            "1 KB",
            GameVersionMd5,
            GameVersionCdnUrl,
            GameVersionCdnUrl,
            IsLatest: true);

        var entry = new GameVersionCatalogEntry(GameVersion, new Dictionary<string, GameVersionAsset>(StringComparer.Ordinal)
        {
            [GamePlatforms.Linux] = asset,
        });

        return new GameVersionCatalog([entry], DateTimeOffset.UtcNow, GameCatalogFreshness.Live);
    }

    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var url = request.RequestUri!;

        if (ModDbOffline && url.Host == "mods.vintagestory.at")
        {
            throw new HttpRequestException("ModDB injoignable (simulé).");
        }

        if (url.Host == "cdn.example")
        {
            return FileResponse(GameVersionArchive, request.Method == HttpMethod.Head, truncate: false);
        }

        if (url.Host == "moddbcdn.vintagestory.at")
        {
            return ModFile(url.AbsolutePath, request.Method == HttpMethod.Head);
        }

        return url.AbsolutePath switch
        {
            "/api/mod/1783" or "/api/mod/configlib" when ConfigLibKnown => FakeHttpMessageHandler.Text(ConfigLibJson),
            "/api/mod/2000" or "/api/mod/vsimgui" => FakeHttpMessageHandler.Text(VsImGuiJson),
            _ => FakeHttpMessageHandler.Text(ModDbSamples.NotFound),
        };
    }

    private HttpResponseMessage ModFile(string path, bool headOnly)
    {
        var payload = path switch
        {
            "/configlib_1.12.0.zip" or "/configlib_1.11.1.zip" => ConfigLibArchive,
            "/vsimgui_1.3.0.zip" => VsImGuiArchive,
            _ => null,
        };

        return payload is null
            ? FakeHttpMessageHandler.Status(HttpStatusCode.NotFound)
            : FileResponse(payload, headOnly, TruncateModDownloads);
    }

    private static HttpResponseMessage FileResponse(byte[] payload, bool headOnly, bool truncate)
    {
        var body = !headOnly && truncate ? payload[..(payload.Length / 2)] : payload;
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        if (headOnly)
        {
            response.Content.Headers.ContentLength = payload.Length;
        }

        return response;
    }

    // Deux releases, comme le vrai échantillon (docs/research/moddb-api.md, ModInstallServiceTests) :
    // 1.12.0 est la plus récente (c'est exactement la version que déclare le modinfo.json embarqué
    // dans ConfigLibArchive), 1.11.1 reste résolvable pour les tests qui la ciblent explicitement.
    private const string ConfigLibJson = """
    {
      "statuscode": "200",
      "mod": {
        "modid": 1783, "assetid": 9551, "name": "Config lib", "text": "<p>lib</p>", "author": "Maltiez",
        "urlalias": null, "logofile": null, "downloads": 627953, "side": "both", "type": "mod",
        "tags": ["Utility"], "lastreleased": "2026-05-01 12:03:34",
        "releases": [
          { "releaseid": 39980, "fileid": 88961, "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.12.0.zip",
            "filename": "configlib_1.12.0.zip", "downloads": 1, "tags": ["1.22.0"], "modidstr": "configlib",
            "modversion": "1.12.0", "changelog": null, "created": "2026-05-01 12:03:34" },
          { "releaseid": 38314, "fileid": 84120, "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.11.1.zip",
            "filename": "configlib_1.11.1.zip", "downloads": 1, "tags": ["1.21.3", "1.21.0"], "modidstr": "configlib",
            "modversion": "1.11.1", "changelog": null, "created": "2026-02-11 09:22:10" }
        ]
      }
    }
    """;

    private const string VsImGuiJson = """
    {
      "statuscode": "200",
      "mod": {
        "modid": 2000, "assetid": 8000, "name": "VS ImGui", "text": "", "author": "Maltiez",
        "urlalias": null, "logofile": null, "downloads": 100, "side": "client", "type": "mod",
        "tags": [], "lastreleased": "2026-01-01 10:00:00",
        "releases": [
          { "releaseid": 30000, "fileid": 70001, "mainfile": "https://moddbcdn.vintagestory.at/vsimgui_1.3.0.zip",
            "filename": "vsimgui_1.3.0.zip", "downloads": 1, "tags": ["1.21.3", "1.21.9", "1.22.0"], "modidstr": "vsimgui",
            "modversion": "1.3.0", "changelog": null, "created": "2026-01-01 10:00:00" }
        ]
      }
    }
    """;
}