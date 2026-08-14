using System.IO.Abstractions;
using System.Text.Json;

using Prospect.Core.GameVersions;
using Prospect.Core.GameVersions.Inno;
using Prospect.Core.Http;

using Shouldly;

using Xunit.Abstractions;

namespace Prospect.Tests.Live;

/// <summary>
/// Étage « conditions réelles » de l'extraction Windows : l'installeur OFFICIEL est ouvert et vidé
/// de son contenu, sans jamais être exécuté.
/// </summary>
/// <remarks>
/// <para>
/// C'est le seul test qui puisse trancher la question qui compte. Les fixtures synthétiques
/// prouvent que le lecteur est cohérent avec une écriture du format faite par nous ; elles ne
/// peuvent pas prouver que cette grille est celle du compilateur Inno Setup. Ici, la preuve est
/// arithmétique et ne laisse aucune place au doute : chaque fichier que l'installeur DÉCLARE est
/// reconstitué, et son empreinte SHA-256 est comparée à celle que l'installeur publie pour lui. Une
/// grille de lecture fausse d'un seul octet ferait tomber la toute première.
/// </para>
/// <para>
/// Relevé du 2026-08-14 sur la 1.22.6 : format de données 6.4.3, 20 098 entrées de fichier,
/// 20 085 emplacements, 862,6 Mio de charge utile en un unique bloc LZMA2 solide, et 20 085
/// empreintes sur 20 085 vérifiées.
/// </para>
/// <para>
/// Le coût est celui d'un vrai téléchargement de 570 Mo, d'où l'opt-in habituel plus une porte
/// supplémentaire : <see cref="InstallerPathVariable"/> permet de désigner un installeur déjà
/// présent sur la machine, et rien n'est téléchargé dans ce cas.
/// </para>
/// </remarks>
[Trait("Category", "Live")]
public sealed class InnoPayloadExtractionLiveTests(ITestOutputHelper output)
{
    /// <summary>Variable qui désigne un installeur déjà téléchargé, pour éviter d'en reprendre un.</summary>
    public const string InstallerPathVariable = "PROSPECT_LIVE_INSTALLER";

    private const string CatalogUrl = "https://api.vintagestory.at/stable.json";

    [LiveFact]
    public async Task TheOfficialInstaller_GivesUpTheWholeGameWithoutBeingRun()
    {
        var fileSystem = new FileSystem();
        var installerPath = await ResolveInstallerAsync(fileSystem);

        var target = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            "prospect-inno-live-" + Guid.NewGuid().ToString("N"));

        try
        {
            var reports = new List<GameInstallProgress>();
            var extractor = new InnoPayloadExtractor(fileSystem);

            await extractor.ExtractAsync(
                installerPath,
                target,
                new CollectingProgress(reports.Add),
                CancellationToken.None);

            var written = fileSystem.Directory.GetFiles(target, "*", SearchOption.AllDirectories);
            output.WriteLine($"{written.Length} fichiers écrits sous {target}");

            // Ce que Prospect attend d'une installation, et ce que sa vérification post-installation
            // contrôlera de toute façon.
            fileSystem.File.Exists(fileSystem.Path.Combine(target, "Vintagestory.exe")).ShouldBeTrue();
            fileSystem.File.Exists(fileSystem.Path.Combine(target, "Vintagestory.dll")).ShouldBeTrue();
            fileSystem.File.Exists(fileSystem.Path.Combine(target, "Vintagestory.runtimeconfig.json")).ShouldBeTrue();
            fileSystem.Directory.Exists(fileSystem.Path.Combine(target, "Lib")).ShouldBeTrue();
            fileSystem.Directory.Exists(fileSystem.Path.Combine(target, "assets")).ShouldBeTrue();

            fileSystem.Directory
                .GetFiles(fileSystem.Path.Combine(target, "assets"), "version-*.txt")
                .ShouldNotBeEmpty();

            // Un installeur du jeu porte des dizaines de milliers d'entrées : un compte à trois
            // chiffres signalerait une lecture qui s'est arrêtée en route sans le dire.
            written.Length.ShouldBeGreaterThan(15_000);

            reports.ShouldNotBeEmpty();
            reports.ShouldAllBe(r => !r.IsEstimated);
            reports[^1].Ratio.ShouldBe(1d);
        }
        finally
        {
            if (fileSystem.Directory.Exists(target))
            {
                fileSystem.Directory.Delete(target, recursive: true);
            }
        }
    }

    private async Task<string> ResolveInstallerAsync(FileSystem fileSystem)
    {
        if (Environment.GetEnvironmentVariable(InstallerPathVariable) is { Length: > 0 } local)
        {
            fileSystem.File.Exists(local).ShouldBeTrue($"{InstallerPathVariable} désigne « {local} », qui n'existe pas.");
            output.WriteLine($"Installeur local : {local}");

            return local;
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(LiveModDb.LiveUserAgent);

        using var catalog = JsonDocument.Parse(await client.GetStringAsync(new Uri(CatalogUrl)));
        var (version, windows) = catalog.RootElement
            .EnumerateObject()
            .Select(p => (p.Name, Entry: p.Value))
            .First(p => p.Entry.TryGetProperty("windows", out _));

        var windowsEntry = windows.GetProperty("windows");
        var url = windowsEntry.GetProperty("urls").GetProperty("cdn").GetString()!;
        var expectedMd5 = windowsEntry.GetProperty("md5").GetString()!;

        var cached = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            windowsEntry.GetProperty("filename").GetString()!);

        if (fileSystem.File.Exists(cached) && Md5Checksum.Matches(expectedMd5, await Md5Async(fileSystem, cached)))
        {
            output.WriteLine($"Installeur {version} déjà en cache : {cached}");

            return cached;
        }

        output.WriteLine($"Téléchargement de l'installeur {version} depuis {url}");
        var response = await client.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var file = fileSystem.File.Create(cached);
        await using (file.ConfigureAwait(false))
        {
            await response.Content.CopyToAsync(file);
        }

        Md5Checksum
            .Matches(expectedMd5, await Md5Async(fileSystem, cached))
            .ShouldBeTrue("l'installeur téléchargé ne correspond pas à l'empreinte du catalogue");

        return cached;
    }

    private static Task<string> Md5Async(FileSystem fileSystem, string path)
        => Md5Checksum.ComputeAsync(fileSystem, path, 1 << 20, CancellationToken.None);

    private sealed class CollectingProgress(Action<GameInstallProgress> report) : IProgress<GameInstallProgress>
    {
        public void Report(GameInstallProgress value) => report(value);
    }
}