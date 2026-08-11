using System.Net;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Gestionnaire HTTP factice injecté dans la composition root des tests : il sert un catalogue de
/// versions du jeu figé, délègue le reste au <see cref="FakeModDbHandler"/> et refuse tout ce qui
/// n'est ni l'un ni l'autre. Aucun test de cet assembly ne peut donc atteindre le réseau réel, ni
/// en local ni en CI.
/// </summary>
internal sealed class FakeCatalogHandler : HttpMessageHandler
{
    /// <summary>Serveur ModDB factice adossé à ce gestionnaire, pour les écrans de mods.</summary>
    public FakeModDbHandler ModDb { get; } = new();

    /// <summary>Extrait de <c>stable.json</c> au format réel (docs/research/vslauncher-et-distribution.md).</summary>
    public const string StableJson = """
    {
      "1.21.3": {
        "windows": { "filename": "vs_install_win-x64_1.21.3.exe", "filesize": "551.0 MB", "md5": "7b8c9d0e1f2a3b4c5d6e7f8091a2b3c4",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_install_win-x64_1.21.3.exe", "local": "https://account.vintagestory.at/files/stable/vs_install_win-x64_1.21.3.exe" }, "latest": 1 },
        "linux": { "filename": "vs_client_linux-x64_1.21.3.tar.gz", "filesize": "570.2 MB", "md5": "6a7b8c9d0e1f2a3b4c5d6e7f8091a2b3",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_client_linux-x64_1.21.3.tar.gz", "local": "https://account.vintagestory.at/files/stable/vs_client_linux-x64_1.21.3.tar.gz" }, "latest": 1 },
        "mac-arm64": { "filename": "vs_client_osx-arm64_1.21.3.tar.gz", "filesize": "588.4 MB", "md5": "5f6a7b8c9d0e1f2a3b4c5d6e7f8091a2",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_client_osx-arm64_1.21.3.tar.gz", "local": "https://account.vintagestory.at/files/stable/vs_client_osx-arm64_1.21.3.tar.gz" }, "latest": 1 }
      },
      "1.20.4": {
        "windows": { "filename": "vs_install_win-x64_1.20.4.exe", "filesize": "540.0 MB", "md5": "aa8c9d0e1f2a3b4c5d6e7f8091a2b3c4",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_install_win-x64_1.20.4.exe", "local": "https://account.vintagestory.at/files/stable/vs_install_win-x64_1.20.4.exe" }, "latest": 0 },
        "linux": { "filename": "vs_client_linux-x64_1.20.4.tar.gz", "filesize": "559.1 MB", "md5": "bb7b8c9d0e1f2a3b4c5d6e7f8091a2b3",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_client_linux-x64_1.20.4.tar.gz", "local": "https://account.vintagestory.at/files/stable/vs_client_linux-x64_1.20.4.tar.gz" }, "latest": 0 },
        "mac-arm64": { "filename": "vs_client_osx-arm64_1.20.4.tar.gz", "filesize": "575.3 MB", "md5": "cc6a7b8c9d0e1f2a3b4c5d6e7f8091a2",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_client_osx-arm64_1.20.4.tar.gz", "local": "https://account.vintagestory.at/files/stable/vs_client_osx-arm64_1.20.4.tar.gz" }, "latest": 0 }
      }
    }
    """;

    /// <summary>Extrait d'<c>unstable.json</c> : une release candidate.</summary>
    public const string UnstableJson = """
    {
      "1.22.0-rc.1": {
        "windows": { "filename": "vs_install_win-x64_1.22.0-rc.1.exe", "filesize": "560.2 MB", "md5": "dd0e1f2a3b4c5d6e7f8091a2b3c4d5e6",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/unstable/vs_install_win-x64_1.22.0-rc.1.exe", "local": "https://account.vintagestory.at/files/unstable/vs_install_win-x64_1.22.0-rc.1.exe" }, "latest": 1 },
        "linux": { "filename": "vs_client_linux-x64_1.22.0-rc.1.tar.gz", "filesize": "578.9 MB", "md5": "ee9d0e1f2a3b4c5d6e7f8091a2b3c4d5",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/unstable/vs_client_linux-x64_1.22.0-rc.1.tar.gz", "local": "https://account.vintagestory.at/files/unstable/vs_client_linux-x64_1.22.0-rc.1.tar.gz" }, "latest": 1 },
        "mac-arm64": { "filename": "vs_client_osx-arm64_1.22.0-rc.1.tar.gz", "filesize": "594.0 MB", "md5": "ff8c9d0e1f2a3b4c5d6e7f8091a2b3c4",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/unstable/vs_client_osx-arm64_1.22.0-rc.1.tar.gz", "local": "https://account.vintagestory.at/files/unstable/vs_client_osx-arm64_1.22.0-rc.1.tar.gz" }, "latest": 1 }
      }
    }
    """;

    /// <summary>Faux : le gestionnaire fait échouer les deux appels de catalogue, pour exercer le mode hors ligne.</summary>
    public bool IsOnline { get; set; } = true;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsOnline)
        {
            throw new HttpRequestException("Réseau simulé indisponible.");
        }

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        var body = path switch
        {
            "/stable.json" => StableJson,
            "/unstable.json" => UnstableJson,
            _ => null,
        };

        if (body is null)
        {
            return Task.FromResult(ModDb.Respond(request));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}