using System.IO.Abstractions;

using Prospect.Core.GameVersions;
using Prospect.Core.GameVersions.Inno;

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
/// peuvent pas prouver que cette grille est celle du compilateur Inno Setup. Ici la preuve est
/// arithmétique et ne laisse aucune place au doute : chaque fichier que l'installeur DÉCLARE est
/// reconstitué, et son empreinte SHA-256 comparée à celle que l'installeur publie pour lui. Une
/// grille fausse d'un seul octet ferait tomber la toute première.
/// </para>
/// <para>
/// Relevé du 2026-08-14 sur la 1.22.6 : format de données 6.4.3, 20 098 entrées de fichier,
/// 20 085 emplacements, 862,6 Mio de charge utile en un unique bloc LZMA2 solide, 20 085 empreintes
/// sur 20 085 vérifiées et 20 084 fichiers posés sous <c>{app}</c> (tout sauf le sondeur de runtime
/// .NET, seule entrée destinée à <c>{tmp}</c>).
/// </para>
/// <para>
/// L'installeur n'est pas téléchargé : voir <see cref="LocalInstallerFactAttribute"/> pour la
/// variable qui le désigne, et pourquoi ce test ne va pas le chercher lui-même.
/// </para>
/// </remarks>
[Trait("Category", "Live")]
public sealed class InnoPayloadExtractionLiveTests(ITestOutputHelper output)
{
    [LocalInstallerFact]
    public async Task TheOfficialInstaller_GivesUpTheWholeGameWithoutBeingRun()
    {
        var installerPath = LocalInstallerFactAttribute.InstallerPath!;
        output.WriteLine($"Installeur lu : {installerPath}");

        var fileSystem = new FileSystem();
        var target = fileSystem.Path.Combine(
            fileSystem.Path.GetTempPath(),
            "prospect-inno-live-" + Guid.NewGuid().ToString("N"));

        try
        {
            var reports = new List<GameInstallProgress>();

            await new InnoPayloadExtractor(fileSystem).ExtractAsync(
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

            fileSystem.Directory
                .GetFiles(fileSystem.Path.Combine(target, "assets"), "version-*.txt")
                .ShouldNotBeEmpty();

            // Un installeur du jeu porte des dizaines de milliers d'entrées : un compte à trois
            // chiffres signalerait une lecture qui s'est arrêtée en route sans le dire.
            written.Length.ShouldBeGreaterThan(15_000);

            // Rien de ce que le script pose hors du dossier du jeu : son sondeur de runtime .NET va
            // dans {tmp}, il n'a aucune raison d'atterrir dans une version installée.
            written.ShouldNotContain(path => path.Contains("netcorecheck", StringComparison.OrdinalIgnoreCase));

            // Les polices, elles, RESTENT. Le script les installe deux fois, dans le dossier de
            // polices du système ET sous {app}, à partir des mêmes entrées de données. Ne pas les
            // poser dans le système ne prive donc le jeu de rien : il embarque les siennes, et c'est
            // ce que ce compte vérifie plutôt que de le supposer.
            fileSystem.Directory
                .GetFiles(fileSystem.Path.Combine(target, "assets", "game", "fonts"), "*.ttf")
                .Length.ShouldBe(11);

            reports.ShouldNotBeEmpty();
            reports.ShouldAllBe(report => !report.IsEstimated && !report.RunsVendorInstaller);
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

    private sealed class CollectingProgress(Action<GameInstallProgress> report) : IProgress<GameInstallProgress>
    {
        public void Report(GameInstallProgress value) => report(value);
    }
}