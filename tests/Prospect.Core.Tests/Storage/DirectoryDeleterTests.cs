using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Storage;
using Prospect.Core.Tests.Instances;

using Shouldly;

namespace Prospect.Core.Tests.Storage;

/// <summary>
/// La suppression récursive partagée par la désinstallation d'une version et la suppression d'une
/// instance. Ce qu'elle doit garantir : effacer réellement, compter honnêtement, et ne jamais faire
/// reculer sa barre.
/// </summary>
public sealed class DirectoryDeleterTests
{
    private const string Root = "/data/prospect/versions/1.22.6";

    private static MockFileSystem Populated(int files, int perFolder = 4)
    {
        var fileSystem = new MockFileSystem();
        for (var index = 0; index < files; index++)
        {
            var folder = index / perFolder;
            fileSystem.AddFile($"{Root}/dossier-{folder}/fichier-{index}.dat", new MockFileData(new byte[16]));
        }

        return fileSystem;
    }

    private static List<DirectoryDeleteProgress> Delete(MockFileSystem fileSystem, string directory = Root)
    {
        var reports = new List<DirectoryDeleteProgress>();
        DirectoryDeleter.Delete(fileSystem, directory, new SynchronousProgress<DirectoryDeleteProgress>(reports.Add));

        return reports;
    }

    [Fact]
    public void APopulatedFolder_IsEmptiedAndCountedFromZeroToOne()
    {
        var fileSystem = Populated(40);

        var reports = Delete(fileSystem);

        fileSystem.Directory.Exists(Root).ShouldBeFalse();
        reports[0].ShouldBe(new DirectoryDeleteProgress(0, 40));
        reports[^1].Ratio.ShouldBe(1d);
        reports[^1].DeletedFiles.ShouldBe(40);
    }

    /// <summary>
    /// Monotone : une barre qui recule est pire qu'une barre absente, et les deux dialogues qui la
    /// portent restent affichés pendant des dizaines de secondes.
    /// </summary>
    [Fact]
    public void TheProgress_NeverGoesBackwards()
    {
        var reports = Delete(Populated(250));

        var ratios = reports.Select(report => report.Ratio).ToArray();
        ratios.ShouldBe(ratios.Order().ToArray());
        ratios[0].ShouldBe(0d);
        ratios[^1].ShouldBe(1d);
    }

    /// <summary>
    /// Le débit de rapports est borné à un par point de pourcentage : chacun traverse le dispatcher
    /// de l'interface, et une instance peut porter des dizaines de milliers de fichiers.
    /// </summary>
    [Fact]
    public void TheProgress_IsThrottledToOneReportPerPercent()
    {
        var reports = Delete(Populated(1000));

        // Cent points de pourcentage, plus le rapport initial et le rapport final.
        reports.Count.ShouldBeLessThanOrEqualTo(102);
    }

    [Fact]
    public void AnEmptyFolder_IsReportedAsAlreadyDone()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Directory.CreateDirectory(Root);

        var reports = Delete(fileSystem);

        fileSystem.Directory.Exists(Root).ShouldBeFalse();
        reports.ShouldContain(report => report.Ratio == 1d);
    }

    [Fact]
    public void AFolderThatIsNotThere_IsNotAnError()
    {
        var reports = Delete(new MockFileSystem());

        reports.ShouldHaveSingleItem().Ratio.ShouldBe(1d);
    }

    /// <summary>
    /// Un fichier qu'on ne peut pas effacer laisse le dossier derrière lui, et le dit. L'appelant
    /// traduit ce verdict dans son propre vocabulaire (instance, version) ; ici on garde le fait.
    /// </summary>
    [Fact]
    public void AFileThatCannotBeDeleted_SurfacesAsAFailureNamingTheFolder()
    {
        var fileSystem = Populated(4);
        fileSystem.File.SetAttributes($"{Root}/dossier-0/fichier-0.dat", FileAttributes.ReadOnly);

        var exception = Should.Throw<DirectoryDeleteFailedException>(() => DirectoryDeleter.Delete(fileSystem, Root));

        exception.Directory.ShouldBe(Root);
        fileSystem.Directory.Exists(Root).ShouldBeTrue();
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Should.Throw<ArgumentNullException>(() => DirectoryDeleter.Delete(null!, Root));
        Should.Throw<ArgumentException>(() => DirectoryDeleter.Delete(new MockFileSystem(), string.Empty));
    }
}