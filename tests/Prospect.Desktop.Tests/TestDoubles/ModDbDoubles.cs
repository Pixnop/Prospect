using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Net;
using System.Text;

using Prospect.Core.Common;
using Prospect.Core.Http;
using Prospect.Core.Instances;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Monte le domaine ModDb sur un système de fichiers factice et un gestionnaire HTTP factice, avec
/// les mêmes classes réelles que la composition root. Les seuls doubles sont ceux des effets de
/// bord : aucun test de cet assembly ne peut atteindre le disque ni le réseau.
/// </summary>
internal static class ModDbDoubles
{
    public static FileSystemInstalledModRepository CreateRepository(
        IFileSystem fileSystem,
        IInstanceRepository instances,
        AppPaths paths)
        => new(
            fileSystem,
            instances,
            new ModArchiveReader(fileSystem),
            new DisabledSuffixModStateConvention(),
            new JsonFileStore(fileSystem));

    public static ModInstallService CreateInstallService(
        IFileSystem fileSystem,
        IInstanceRepository instances,
        IInstalledModRepository mods,
        AppPaths paths,
        IClock clock,
        HttpMessageHandler? handler = null)
    {
        handler ??= new FakeModDbHandler();
        var client = new ModDbClient(
            new HttpClient(handler, disposeHandler: false),
            new JsonFileStore(fileSystem),
            paths,
            clock,
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask));
        var downloads = new DownloadManager(new HttpClient(handler, disposeHandler: false), fileSystem, paths, clock);

        return new ModInstallService(client, downloads, mods, instances, new ModArchiveReader(fileSystem), fileSystem, clock);
    }

    public static IModDbClient CreateClient(
        IFileSystem fileSystem,
        AppPaths paths,
        IClock clock,
        HttpMessageHandler? handler = null)
        => new ModDbClient(
            new HttpClient(handler ?? new FakeModDbHandler(), disposeHandler: false),
            new JsonFileStore(fileSystem),
            paths,
            clock,
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask));

    /// <summary>Pose une archive de mod valide dans le dossier <c>data/Mods/</c> d'une instance.</summary>
    public static void SeedMod(MockFileSystem fileSystem, string modsDirectory, string fileName, string modInfoJson)
        => fileSystem.AddFile(fileSystem.Path.Combine(modsDirectory, fileName), new MockFileData(BuildArchive(modInfoJson)));

    /// <summary>Construit une archive de mod en mémoire, avec son <c>modinfo.json</c> à la racine.</summary>
    public static byte[] BuildArchive(string? modInfoJson)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (modInfoJson is not null)
            {
                var entry = archive.CreateEntry("modinfo.json");
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(modInfoJson));
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Un <c>modinfo.json</c> minimal mais valide.</summary>
    public static string ModInfo(string modId, string name, string version = "1.0.0", string? dependency = null)
    {
        var dependencies = dependency is null ? "{}" : $$"""{ "{{dependency}}": "" }""";

        return $$"""
        { "type": "code", "modid": "{{modId}}", "name": "{{name}}", "version": "{{version}}",
          "authors": ["Quelqu'un"], "side": "universal", "dependencies": {{dependencies}} }
        """;
    }
}

/// <summary>
/// Serveur ModDB factice : un catalogue de deux mods, leurs fiches, leurs releases et les archives
/// correspondantes. <see cref="IsOnline"/> à faux fait échouer tous les appels, ce qui exerce le
/// bandeau « ModDB injoignable » de la maquette.
/// </summary>
internal sealed class FakeModDbHandler : HttpMessageHandler
{
    public const string CatalogJson = """
    {
      "statuscode": "200",
      "mods": [
        { "modid": 1783, "assetid": 9551, "downloads": 627953, "follows": 0, "trendingpoints": 5, "comments": 0,
          "name": "Config lib", "summary": "A universal place to configure your mods.", "modidstrs": ["configlib"],
          "author": "Maltiez", "urlalias": null, "side": "both", "type": "mod", "logo": null,
          "tags": ["Utility"], "lastreleased": "2026-05-01 12:03:34" },
        { "modid": 792, "assetid": 3829, "downloads": 1095320, "follows": 8133, "trendingpoints": 0, "comments": 1440,
          "name": "BetterRuins", "summary": "Adds many new ruins to your survival world.", "modidstrs": ["betterruins"],
          "author": "NiclAss", "urlalias": "betterruins", "side": "both", "type": "mod",
          "logo": "https://moddbcdn.vintagestory.at/betterruins.png",
          "tags": ["Worldgen", "Exploration"], "lastreleased": "2026-07-28 18:59:32" }
      ]
    }
    """;

    public const string TagsJson = """
    {
      "statuscode": "200",
      "tags": [
        {"tagid": "12", "name": "Utility", "color": "#92C96AFF"},
        {"tagid": "18", "name": "Worldgen", "color": "#92C96AFF"},
        {"tagid": "22", "name": "Exploration", "color": "#92C96AFF"}
      ]
    }
    """;

    private const string ConfigLibJson = """
    {
      "statuscode": "200",
      "mod": {
        "modid": 1783, "assetid": 9551, "name": "Config lib",
        "text": "<h2>Config lib</h2><p>A universal place to configure your mods.</p>",
        "author": "Maltiez", "urlalias": null, "logofile": null, "downloads": 627953,
        "side": "both", "type": "mod", "tags": ["Utility"], "lastreleased": "2026-05-01 12:03:34",
        "releases": [
          { "releaseid": 38314, "fileid": 84120, "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.11.1.zip",
            "filename": "configlib_1.11.1.zip", "downloads": 90210, "tags": ["1.21.3"], "modidstr": "configlib",
            "modversion": "1.11.1", "changelog": null, "created": "2026-02-11 09:22:10" }
        ]
      }
    }
    """;

    private const string BetterRuinsJson = """
    {
      "statuscode": "200",
      "mod": {
        "modid": 792, "assetid": 3829, "name": "BetterRuins", "text": "<p>Ruines</p>",
        "author": "NiclAss", "urlalias": "betterruins", "logofile": null, "downloads": 1095320,
        "side": "both", "type": "mod", "tags": ["Worldgen"], "lastreleased": "2026-07-28 18:59:32",
        "releases": [
          { "releaseid": 41000, "fileid": 91000, "mainfile": "https://moddbcdn.vintagestory.at/betterruins_2.0.0.zip",
            "filename": "betterruins_2.0.0.zip", "downloads": 5, "tags": ["1.22.0"], "modidstr": "betterruins",
            "modversion": "2.0.0", "changelog": null, "created": "2026-07-28 18:59:32" }
        ]
      }
    }
    """;

    private static readonly byte[] ConfigLibArchive = ModDbDoubles.BuildArchive(ModDbDoubles.ModInfo("configlib", "Config lib", "1.11.1"));

    /// <summary>Faux pour simuler un ModDB injoignable.</summary>
    public bool IsOnline { get; set; } = true;

    /// <summary>Identifiants de mods à considérer comme compatibles avec la version demandée.</summary>
    public IReadOnlyList<int> CompatibleModIds { get; set; } = [1783, 792];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(Respond(request));

    /// <summary>Répond à une requête ModDB. Exposé pour que <see cref="FakeCatalogHandler"/> délègue ici.</summary>
    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        if (!IsOnline)
        {
            throw new HttpRequestException("Réseau simulé indisponible.");
        }

        var url = request.RequestUri!;

        if (url.Host == "moddbcdn.vintagestory.at")
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ConfigLibArchive) };
            if (request.Method == HttpMethod.Head)
            {
                response.Content.Headers.ContentLength = ConfigLibArchive.Length;
            }

            return response;
        }

        var body = url.AbsolutePath switch
        {
            "/api/mods" when url.Query.Length > 0 => FilteredCatalog(),
            "/api/mods" => CatalogJson,
            "/api/tags" => TagsJson,
            "/api/mod/1783" or "/api/mod/configlib" => ConfigLibJson,
            "/api/mod/792" or "/api/mod/betterruins" => BetterRuinsJson,
            "/api/v2/mods/install-information" => """{ "data": {} }""",
            _ => """{"statuscode":"404"}""",
        };

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    private string FilteredCatalog()
    {
        var entries = CompatibleModIds
            .Select(modId => $$"""
            { "modid": {{modId}}, "assetid": 1, "downloads": 1, "follows": 0, "trendingpoints": 0, "comments": 0,
              "name": "mod {{modId}}", "summary": "", "modidstrs": [], "author": "", "urlalias": null,
              "side": "both", "type": "mod", "logo": null, "tags": [], "lastreleased": null }
            """);

        return $$"""{ "statuscode": "200", "mods": [ {{string.Join(',', entries)}} ] }""";
    }
}