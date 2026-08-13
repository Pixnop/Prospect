using System.Formats.Tar;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Storage;

using Shouldly;

using Xunit.Abstractions;

namespace Prospect.Tests.Live;

/// <summary>
/// Étage « conditions réelles » de la TOPOLOGIE des archives du jeu. Aucun double ne peut trancher
/// cette question : seule l'archive publiée sait si son contenu est à plat ou sous un dossier
/// racine, et c'est précisément la réponse que nos fixtures avaient devinée à l'envers pendant
/// toute la vie du projet. Aucune installation Linux n'a jamais abouti, et le test qui aurait dû
/// le dire modélisait un tar sans dossier racine.
/// </summary>
/// <remarks>
/// <para>
/// Relevé du 2026-08-13 sur la 1.22.6, que ce test rejoue : <c>vs_client_linux-x64</c> commence par
/// <c>vintagestory/</c>, <c>vs_client_osx-x64</c> et <c>vs_client_osx-arm64</c> par
/// <c>Vintage Story.app/</c>. Un seul dossier racine dans les trois cas, donc la règle
/// d'aplatissement de <c>TarGzGameInstaller</c> s'applique bien à toutes.
/// </para>
/// <para>
/// Le coût réseau est borné à <see cref="ProbeBytes"/> par archive au lieu des 590 à 614 Mo
/// annoncés par le catalogue : les en-têtes <c>Range</c> sont supportés par les deux miroirs
/// officiels (c'est ce sur quoi repose la reprise de <c>DownloadManager</c>), et les premières
/// entrées d'un tar suffisent à lire sa topologie. Le flux gzip est forcément tronqué à la fin,
/// d'où la lecture tolérante de <see cref="ReadEntryNamesAsync"/>.
/// </para>
/// <para>
/// Mêmes garde-fous de politesse que <see cref="LiveModDb"/> : requêtes espacées de
/// <see cref="LiveModDb.MinimumInterval"/> et <c>User-Agent</c> reconnaissable.
/// </para>
/// </remarks>
[Trait("Category", "Live")]
public sealed class GameArchiveLayoutLiveTests(ITestOutputHelper output)
{
    /// <summary>Taille de la fenêtre demandée en tête d'archive. 300 Ko couvrent ~1 900 entrées.</summary>
    private const int ProbeBytes = 300_000;

    /// <summary>
    /// Les trois archives CLIENT que Prospect installe, dans l'ordre où le catalogue les nomme.
    /// Le serveur dédié et l'installeur Windows sont hors périmètre : l'un n'est pas un client,
    /// l'autre n'est pas un tar.
    /// </summary>
    private static readonly string[] ClientPlatformKeys = [GamePlatforms.Linux, GamePlatforms.MacX64, GamePlatforms.MacArm64];

    [LiveFact]
    public async Task EveryClientArchive_PutsEverythingUnderASingleRootFolder()
    {
        using var probe = CreateProbe();
        var entry = await LatestStableAsync(probe);
        output.WriteLine($"Version interrogée : {entry.Version}");

        foreach (var platformKey in ClientPlatformKeys)
        {
            var asset = entry.Assets[platformKey];
            var names = await ReadEntryNamesAsync(probe, asset.CdnUrl);

            var roots = names
                .Select(name => name.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                .Where(root => root is not null)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            output.WriteLine(
                $"{platformKey} ({asset.FileName}, {asset.DisplaySize} annoncés) : {names.Count} entrées lues sur {ProbeBytes} octets, "
                + $"racine(s) « {string.Join("», «", roots)} », premières entrées : {string.Join(", ", names.Take(4))}");

            // L'affirmation qui tient toute la correction : une seule racine, et elle est un
            // DOSSIER (des entrées existent en dessous). C'est exactement la condition sous
            // laquelle TarGzGameInstaller aplatit.
            roots.ShouldHaveSingleItem($"{asset.FileName} devrait n'avoir qu'un dossier racine");
            names.ShouldContain(name => name.Contains('/', StringComparison.Ordinal), customMessage: $"{asset.FileName} devrait contenir des entrées sous sa racine");
        }
    }

    /// <summary>
    /// Le corollaire côté stratégie : une fois la racine aplatie, l'exécutable attendu par
    /// <see cref="IGameInstallStrategy.ExpectedExecutables"/> est bien une entrée de l'archive.
    /// C'est ce qui relie le relevé de topologie à la vérification post-installation qui échouait.
    /// </summary>
    [LiveFact]
    public async Task LinuxArchive_OnceItsRootFolderIsStripped_CarriesTheExecutableWeExpect()
    {
        using var probe = CreateProbe();
        var entry = await LatestStableAsync(probe);
        var asset = entry.Assets[GamePlatforms.Linux];

        var names = await ReadEntryNamesAsync(probe, asset.CdnUrl, ProbeBytes * 10);
        var stripped = names
            .Select(name => name.Split('/', 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(segments => segments.Length == 2)
            .Select(segments => segments[1])
            .ToArray();

        var expected = new LinuxGameInstallStrategy(new FileSystem(), new SystemUnixFilePermissions())
            .ExpectedExecutables
            .Select(location => location.ToString())
            .ToArray();

        output.WriteLine($"{asset.FileName} : {stripped.Length} entrées après aplatissement, attendus « {string.Join("», «", expected)} »");
        stripped.ShouldContain(name => expected.Contains(name, StringComparer.Ordinal));
    }

    private static async Task<GameVersionCatalogEntry> LatestStableAsync(HttpClient probe)
    {
        var root = Path.Combine(Path.GetTempPath(), "prospect-live-" + Guid.NewGuid().ToString("N"));
        var fileSystem = new FileSystem();
        fileSystem.Directory.CreateDirectory(root);

        try
        {
            using var catalog = new HttpGameVersionCatalog(
                probe,
                new JsonFileStore(fileSystem),
                new AppPaths(new SystemAppEnvironment(), root),
                new SystemClock());

            var versions = await catalog.GetAsync(forceRefresh: true, CancellationToken.None);

            return versions.Versions.First(candidate => candidate.Channel == GameVersionChannel.Stable);
        }
        finally
        {
            try
            {
                fileSystem.Directory.Delete(root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Un cache temporaire non supprimé n'invalide aucun test : l'OS le nettoiera.
            }
        }
    }

    /// <summary>
    /// Noms des entrées lisibles dans les <paramref name="byteCount"/> premiers octets de l'archive.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadEntryNamesAsync(HttpClient probe, Uri url, int byteCount = ProbeBytes)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.Range = new RangeHeaderValue(0, byteCount - 1);

        using var response = await probe.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
        response.StatusCode.ShouldBe(HttpStatusCode.PartialContent, $"{url} devrait honorer une requête de plage");

        var window = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        await using (window.ConfigureAwait(false))
        {
            var decompressed = new GZipStream(window, CompressionMode.Decompress);
            await using (decompressed.ConfigureAwait(false))
            {
                var reader = new TarReader(decompressed, leaveOpen: true);
                await using (reader.ConfigureAwait(false))
                {
                    var names = new List<string>();
                    try
                    {
                        while (await reader.GetNextEntryAsync(copyData: false, CancellationToken.None) is { } entry)
                        {
                            names.Add(entry.Name.Replace('\\', '/').TrimStart('/'));
                        }
                    }
                    catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or IOException)
                    {
                        // Attendu : la fenêtre demandée coupe le flux gzip en plein milieu. Tout ce
                        // qui précède la coupure reste valide, et c'est tout ce dont ce test a besoin.
                    }

                    names.ShouldNotBeEmpty($"{url} devrait livrer au moins une entrée lisible");

                    return names;
                }
            }
        }
    }

    // Client à part de celui de LiveModDb : ce sont d'autres hôtes (api et cdn.vintagestory.at),
    // et l'espacement de LiveModDb ne couvre que son propre handler.
    private static HttpClient CreateProbe()
    {
        var probe = new HttpClient(new SpacedHandler()) { Timeout = TimeSpan.FromMinutes(2) };
        probe.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", LiveModDb.LiveUserAgent);

        return probe;
    }

    /// <summary>
    /// Sérialise les requêtes et impose <see cref="LiveModDb.MinimumInterval"/> entre deux départs,
    /// même discipline et même raison d'être que le handler de <see cref="LiveModDb"/>.
    /// </summary>
    private sealed class SpacedHandler() : DelegatingHandler(new HttpClientHandler())
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTimeOffset _lastDeparture = DateTimeOffset.MinValue;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var wait = _lastDeparture + LiveModDb.MinimumInterval - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                }

                _lastDeparture = DateTimeOffset.UtcNow;
            }
            finally
            {
                _gate.Release();
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gate.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}