using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.GameVersions;
using Prospect.Core.Tests.Instances;

using Shouldly;

namespace Prospect.Core.Tests.GameVersions;

/// <summary>
/// L'estimation d'avancement de l'installeur Windows. Ce qui compte n'est pas l'exactitude du
/// chiffre — c'est une estimation assumée — mais qu'il ne mente jamais : jamais décroissant, jamais
/// 100 % avant que le processus n'ait rendu la main, jamais d'exception quand le dossier n'est pas
/// là.
/// </summary>
public sealed class InstallDirectoryGrowthReporterTests
{
    private const string InstallerPath = "/cache/downloads/vs_setup.exe";
    private const string TargetDirectory = "/data/prospect/versions/1.22.6";

    private static MockFileSystem WithInstaller(int installerBytes = 1000)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(InstallerPath, new MockFileData(new byte[installerBytes]));

        return fileSystem;
    }

    private static void Write(MockFileSystem fileSystem, string name, int bytes)
        => fileSystem.AddFile(fileSystem.Path.Combine(TargetDirectory, name), new MockFileData(new byte[bytes]));

    private static (InstallDirectoryGrowthReporter Reporter, List<GameInstallProgress> Reports) Create(MockFileSystem fileSystem)
    {
        var reports = new List<GameInstallProgress>();
        var reporter = InstallDirectoryGrowthReporter
            .TryCreate(fileSystem, InstallerPath, TargetDirectory, new SynchronousProgress<GameInstallProgress>(reports.Add))
            .ShouldNotBeNull();

        return (reporter, reports);
    }

    [Fact]
    public void AGrowingDirectory_PublishesRisingRatios()
    {
        var fileSystem = WithInstaller();
        var (reporter, reports) = Create(fileSystem);

        // 1000 octets d'installeur × 1,8 = 1800 octets attendus.
        Write(fileSystem, "a.dll", 180);
        reporter.Sample();
        Write(fileSystem, "b.dll", 720);
        reporter.Sample();

        reports.Select(report => report.Ratio).ShouldBe([0.1d, 0.5d]);
        reports.ShouldAllBe(report => report.Phase == GameInstallPhase.Installing);
        reports.ShouldAllBe(report => report.IsEstimated);
    }

    /// <summary>
    /// Une barre qui recule est pire qu'une barre absente. Un installeur qui remplace un fichier
    /// temporaire par un plus petit ne doit jamais produire ce spectacle.
    /// </summary>
    [Fact]
    public void ADirectoryThatShrinks_NeverPublishesALowerRatio()
    {
        var fileSystem = WithInstaller();
        var (reporter, reports) = Create(fileSystem);

        Write(fileSystem, "gros.tmp", 900);
        reporter.Sample();
        fileSystem.RemoveFile(fileSystem.Path.Combine(TargetDirectory, "gros.tmp"));
        Write(fileSystem, "petit.dll", 90);
        reporter.Sample();

        reports.ShouldHaveSingleItem().Ratio.ShouldBe(0.5d);
    }

    /// <summary>
    /// Seul le retour du processus autorise à dire que c'est fini. Un dossier plus gros que prévu
    /// (facteur d'expansion sous-estimé) plafonne, il ne dépasse pas.
    /// </summary>
    [Fact]
    public void ADirectoryBiggerThanExpected_StopsBelowOneHundredPercent()
    {
        var fileSystem = WithInstaller();
        var (reporter, reports) = Create(fileSystem);

        Write(fileSystem, "tout.dll", 9000);
        reporter.Sample();

        reports.ShouldHaveSingleItem().Ratio.ShouldBe(InstallDirectoryGrowthReporter.MaxReportedRatio);
    }

    [Fact]
    public void ATargetDirectoryThatDoesNotExistYet_PublishesNothingRatherThanFailing()
    {
        var fileSystem = WithInstaller();
        var (reporter, reports) = Create(fileSystem);

        reporter.Sample();

        reports.ShouldBeEmpty();
    }

    [Fact]
    public void NoProgressObserver_MeansNoReporterAtAll()
        => InstallDirectoryGrowthReporter.TryCreate(WithInstaller(), InstallerPath, TargetDirectory, progress: null).ShouldBeNull();

    /// <summary>
    /// Sans taille d'installeur, aucun dénominateur : on retombe sur l'ancien comportement, une
    /// phase franchement indéterminée. Inventer un dénominateur serait inventer un pourcentage.
    /// </summary>
    [Fact]
    public void AnUnreadableInstaller_MeansNoReporterAtAll()
    {
        var reports = new List<GameInstallProgress>();

        InstallDirectoryGrowthReporter
            .TryCreate(new MockFileSystem(), InstallerPath, TargetDirectory, new SynchronousProgress<GameInstallProgress>(reports.Add))
            .ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_SamplesUntilItIsCanceledThenReturnsCleanly()
    {
        var fileSystem = WithInstaller();
        var (reporter, reports) = Create(fileSystem);
        using var stop = new CancellationTokenSource();
        var ticks = 0;

        var loop = reporter.RunAsync(
            TimeSpan.FromSeconds(1),
            (_, _) =>
            {
                // Trois battements, puis on coupe : c'est l'installeur qui rend la main.
                Write(fileSystem, $"part-{ticks}.dll", 180);
                if (++ticks >= 3)
                {
                    stop.Cancel();
                }

                return Task.CompletedTask;
            },
            stop.Token);

        await loop;

        reports.Count.ShouldBe(3);
        reports.Select(report => report.Ratio).ShouldBe(reports.Select(report => report.Ratio).Order().ToArray());
    }

    [Fact]
    public void TryCreate_NullFileSystem_IsRejected()
        => Should.Throw<ArgumentNullException>(() => InstallDirectoryGrowthReporter.TryCreate(null!, InstallerPath, TargetDirectory, null));
}