using System.Diagnostics.CodeAnalysis;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Une archive de jeu Linux minuscule mais RÉELLE (tar.gz écrit par la BCL), avec son empreinte
/// md5 calculée sur ses octets. Elle existe pour qu'un parcours puisse installer une version du
/// jeu de bout en bout sur le conteneur réel : <c>GameInstallService</c> vérifie l'empreinte
/// annoncée par le catalogue avant d'extraire, donc un catalogue et un CDN factices doivent
/// s'accorder sur une empreinte VRAIE plutôt que sur une chaîne inventée.
/// </summary>
[SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "Miroir du contrôle d'intégrité imposé par le catalogue officiel (voir Prospect.Core.Http.Md5Checksum) : aucun usage de sécurité.")]
[SuppressMessage("Major Code Smell", "S4790:Using weak hashing algorithms is security-sensitive", Justification = "Miroir du contrôle d'intégrité imposé par le catalogue officiel (voir Prospect.Core.Http.Md5Checksum) : aucun usage de sécurité.")]
internal static class FakeGameArchive
{
    /// <summary>Octets de l'archive, contenant l'exécutable que la stratégie Linux attend.</summary>
    public static byte[] LinuxTarGz { get; } = Build();

    /// <summary>Empreinte md5 des octets ci-dessus, en minuscules, telle que le catalogue l'annonce.</summary>
    public static string LinuxMd5 { get; } = Convert.ToHexStringLower(MD5.HashData(LinuxTarGz));

    private static byte[] Build()
    {
        using var output = new MemoryStream();
        using (var compressed = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            using var writer = new TarWriter(compressed, TarEntryFormat.Pax, leaveOpen: true);
            Write(writer, "Vintagestory", "#!/bin/sh\necho jeu");
            Write(writer, "Vintagestory.dll", "assembly");
            Write(writer, "assets/game/lang/fr.json", "{}");
        }

        return output.ToArray();
    }

    private static void Write(TarWriter writer, string name, string content)
    {
        using var data = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = data });
    }
}

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

    /// <summary>Service de compte factice, pour la section Comptes des Réglages.</summary>
    public FakeVsAuthHandler Auth { get; } = new();

    /// <summary>
    /// Extrait de <c>stable.json</c> au format réel (docs/research/vslauncher-et-distribution.md).
    /// Les empreintes des archives LINUX sont celles de <see cref="FakeGameArchive"/>, qui est
    /// vraiment servie par ce gestionnaire : c'est ce qui rend une installation de version
    /// jouable de bout en bout dans un parcours, vérification d'empreinte comprise.
    /// </summary>
    public static string StableJson { get; } = $$"""
    {
      "1.21.3": {
        "windows": { "filename": "vs_install_win-x64_1.21.3.exe", "filesize": "551.0 MB", "md5": "7b8c9d0e1f2a3b4c5d6e7f8091a2b3c4",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_install_win-x64_1.21.3.exe", "local": "https://account.vintagestory.at/files/stable/vs_install_win-x64_1.21.3.exe" }, "latest": 1 },
        "linux": { "filename": "vs_client_linux-x64_1.21.3.tar.gz", "filesize": "570.2 MB", "md5": "{{FakeGameArchive.LinuxMd5}}",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_client_linux-x64_1.21.3.tar.gz", "local": "https://account.vintagestory.at/files/stable/vs_client_linux-x64_1.21.3.tar.gz" }, "latest": 1 },
        "mac-arm64": { "filename": "vs_client_osx-arm64_1.21.3.tar.gz", "filesize": "588.4 MB", "md5": "5f6a7b8c9d0e1f2a3b4c5d6e7f8091a2",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_client_osx-arm64_1.21.3.tar.gz", "local": "https://account.vintagestory.at/files/stable/vs_client_osx-arm64_1.21.3.tar.gz" }, "latest": 1 }
      },
      "1.20.4": {
        "windows": { "filename": "vs_install_win-x64_1.20.4.exe", "filesize": "540.0 MB", "md5": "aa8c9d0e1f2a3b4c5d6e7f8091a2b3c4",
          "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_install_win-x64_1.20.4.exe", "local": "https://account.vintagestory.at/files/stable/vs_install_win-x64_1.20.4.exe" }, "latest": 0 },
        "linux": { "filename": "vs_client_linux-x64_1.20.4.tar.gz", "filesize": "559.1 MB", "md5": "{{FakeGameArchive.LinuxMd5}}",
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

        // Le service de compte a son propre hôte : le router ici plutôt que dans le ModDB garde la
        // garantie centrale de cet assembly, à savoir qu'AUCUNE requête ne peut sortir, pas même une
        // tentative de connexion.
        if (request.RequestUri?.Host == "auth3.vintagestory.at")
        {
            return Task.FromResult(Auth.Respond(request));
        }

        // Le CDN des fichiers du jeu : servi ici plutôt que délégué au ModDB, pour que
        // l'installation d'une version soit jouable en entier dans un parcours (téléchargement,
        // vérification d'empreinte, extraction, sentinelle de complétude).
        if (request.RequestUri?.Host is "cdn.vintagestory.at" or "account.vintagestory.at")
        {
            var archive = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(request.Method == HttpMethod.Head ? [] : FakeGameArchive.LinuxTarGz),
            };
            archive.Content.Headers.ContentLength = FakeGameArchive.LinuxTarGz.Length;

            return Task.FromResult(archive);
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