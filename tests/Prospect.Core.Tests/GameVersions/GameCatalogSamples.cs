namespace Prospect.Core.Tests.GameVersions;

/// <summary>
/// Échantillons du catalogue officiel repris de docs/research/vslauncher-et-distribution.md
/// (section b, structure relevée en live le 2026-08-10) : mêmes clés, mêmes formes de valeurs,
/// mêmes miroirs. Ce sont eux qui nourrissent les tests de désérialisation, pour que le contrat
/// vérifié soit celui de l'API réelle et pas celui qu'on aurait aimé qu'elle ait.
/// </summary>
internal static class GameCatalogSamples
{
    /// <summary>Extrait de <c>stable.json</c> : deux versions, les sept clés de plateforme observées.</summary>
    public const string Stable = """
    {
      "1.22.6": {
        "windows": {
          "filename": "vs_install_win-x64_1.22.6.exe",
          "filesize": "570.4 MB",
          "md5": "0ca071fa9b3d4e5f8a1c2b3d4e5f6a7b",
          "urls": {
            "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_install_win-x64_1.22.6.exe",
            "local": "https://account.vintagestory.at/files/stable/vs_install_win-x64_1.22.6.exe"
          },
          "latest": 1
        },
        "windowsupdate": {
          "filename": "vs_update_win-x64_1.22.6.exe",
          "filesize": "107.3 MB",
          "md5": "1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e",
          "urls": {
            "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_update_win-x64_1.22.6.exe",
            "local": "https://account.vintagestory.at/files/stable/vs_update_win-x64_1.22.6.exe"
          },
          "latest": 1
        },
        "linux": {
          "filename": "vs_client_linux-x64_1.22.6.tar.gz",
          "filesize": "590.5 MB",
          "md5": "c00c436c7d8e9f0a1b2c3d4e5f6a7b8c",
          "urls": {
            "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_client_linux-x64_1.22.6.tar.gz",
            "local": "https://account.vintagestory.at/files/stable/vs_client_linux-x64_1.22.6.tar.gz"
          },
          "latest": 1
        },
        "linuxserver": {
          "filename": "vs_server_linux-x64_1.22.6.tar.gz",
          "filesize": "51.4 MB",
          "md5": "2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f",
          "urls": {
            "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_server_linux-x64_1.22.6.tar.gz",
            "local": "https://account.vintagestory.at/files/stable/vs_server_linux-x64_1.22.6.tar.gz"
          },
          "latest": 1
        },
        "windowsserver": {
          "filename": "vs_server_win-x64_1.22.6.zip",
          "filesize": "61.4 MB",
          "md5": "3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f80",
          "urls": {
            "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_server_win-x64_1.22.6.zip",
            "local": "https://account.vintagestory.at/files/stable/vs_server_win-x64_1.22.6.zip"
          },
          "latest": 1
        },
        "mac-x64": {
          "filename": "vs_client_osx-x64_1.22.6.tar.gz",
          "filesize": "613.8 MB",
          "md5": "4e5f6a7b8c9d0e1f2a3b4c5d6e7f8091",
          "urls": {
            "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_client_osx-x64_1.22.6.tar.gz",
            "local": "https://account.vintagestory.at/files/stable/vs_client_osx-x64_1.22.6.tar.gz"
          },
          "latest": 1
        },
        "mac-arm64": {
          "filename": "vs_client_osx-arm64_1.22.6.tar.gz",
          "filesize": "608.1 MB",
          "md5": "5f6a7b8c9d0e1f2a3b4c5d6e7f8091a2",
          "urls": {
            "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_client_osx-arm64_1.22.6.tar.gz",
            "local": "https://account.vintagestory.at/files/stable/vs_client_osx-arm64_1.22.6.tar.gz"
          },
          "latest": 1
        }
      },
      "1.21.3": {
        "linux": {
          "filename": "vs_client_linux-x64_1.21.3.tar.gz",
          "filesize": "570.2 MB",
          "md5": "6a7b8c9d0e1f2a3b4c5d6e7f8091a2b3",
          "urls": {
            "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_client_linux-x64_1.21.3.tar.gz",
            "local": "https://account.vintagestory.at/files/stable/vs_client_linux-x64_1.21.3.tar.gz"
          },
          "latest": 0
        },
        "windows": {
          "filename": "vs_install_win-x64_1.21.3.exe",
          "filesize": "551.0 MB",
          "md5": "7b8c9d0e1f2a3b4c5d6e7f8091a2b3c4",
          "urls": {
            "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_install_win-x64_1.21.3.exe",
            "local": "https://account.vintagestory.at/files/stable/vs_install_win-x64_1.21.3.exe"
          },
          "latest": 0
        }
      }
    }
    """;

    /// <summary>Extrait d'<c>unstable.json</c> : une release candidate, nommée <c>X.Y.Z-rc.N</c> comme dans le lot réel.</summary>
    public const string Unstable = """
    {
      "1.23.0-rc.1": {
        "linux": {
          "filename": "vs_client_linux-x64_1.23.0-rc.1.tar.gz",
          "filesize": "601.9 MB",
          "md5": "8c9d0e1f2a3b4c5d6e7f8091a2b3c4d5",
          "urls": {
            "cdn": "https://cdn.vintagestory.at/gamefiles/unstable/vs_client_linux-x64_1.23.0-rc.1.tar.gz",
            "local": "https://account.vintagestory.at/files/unstable/vs_client_linux-x64_1.23.0-rc.1.tar.gz"
          },
          "latest": 1
        },
        "windows": {
          "filename": "vs_install_win-x64_1.23.0-rc.1.exe",
          "filesize": "580.7 MB",
          "md5": "9d0e1f2a3b4c5d6e7f8091a2b3c4d5e6",
          "urls": {
            "cdn": "https://cdn.vintagestory.at/gamefiles/unstable/vs_install_win-x64_1.23.0-rc.1.exe",
            "local": "https://account.vintagestory.at/files/unstable/vs_install_win-x64_1.23.0-rc.1.exe"
          },
          "latest": 1
        }
      }
    }
    """;

    /// <summary>Document vide, la forme que rend l'API quand un canal n'a rien à publier.</summary>
    public const string Empty = "{}";
}