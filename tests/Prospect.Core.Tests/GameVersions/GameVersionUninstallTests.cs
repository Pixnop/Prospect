using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

using NSubstitute;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Instances;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.GameVersions;

/// <summary>
/// La désinstallation d'une version du jeu, sous l'angle du défaut rapporté en test réel :
/// « désinstaller a lagué un bon coup ». Six cents mégaoctets effacés sur le thread appelant, exactement
/// le même défaut que la suppression d'instance, et corrigé de la même façon — plus une barre qui
/// compte, puisqu'on peut relever les fichiers avant de les effacer.
/// </summary>
/// <remarks>
/// Le système de fichiers est substitué UNIQUEMENT sur son <see cref="IDirectory"/>, pour tenir la
/// suppression ouverte et observer ce qui se passe pendant ; tout le reste va au
/// <see cref="MockFileSystem"/> réel. Même harnais qu'<c>InstanceDeletionTests</c>.
/// </remarks>
public sealed class GameVersionUninstallTests : IDisposable
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly GameVersion Version = GameVersion.Parse("1.22.6");

    private readonly ManualResetEventSlim _gate = new(false);
    private readonly ManualResetEventSlim _entered = new(false);

    private int _deleteThreadId;

    public void Dispose()
    {
        _gate.Dispose();
        _entered.Dispose();
    }

    private static MockFileSystem Installed(int files)
    {
        var fileSystem = new MockFileSystem();
        var directory = fileSystem.Path.Combine(Paths.VersionsDirectory, Version.ToString());

        for (var index = 0; index < files; index++)
        {
            fileSystem.AddFile(fileSystem.Path.Combine(directory, "assets", $"fichier-{index}.dat"), new MockFileData(new byte[64]));
        }

        fileSystem.AddFile(
            fileSystem.Path.Combine(directory, FileSystemInstalledGameVersionRepository.CompletionMarkerFileName),
            new MockFileData(Version.ToString()));

        return fileSystem;
    }

    /// <summary>
    /// La barre monte, ne redescend jamais, et finit à 1. C'est tout ce qu'on demande à une
    /// estimation qui n'en est pas une : ici le dénominateur est exact.
    /// </summary>
    [Fact]
    public async Task RemoveAsync_ReportsAMonotoneProgressEndingAtOne()
    {
        var fileSystem = Installed(120);
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var reports = new List<DirectoryDeleteProgress>();

        await repository.RemoveAsync(Version, new SynchronousProgress<DirectoryDeleteProgress>(reports.Add), CancellationToken.None);

        repository.IsInstalled(Version).ShouldBeFalse();
        fileSystem.Directory.Exists(repository.GetVersionDirectory(Version)).ShouldBeFalse();

        var ratios = reports.Select(report => report.Ratio).ToArray();
        ratios.ShouldBe(ratios.Order().ToArray());
        ratios[0].ShouldBe(0d);
        ratios[^1].ShouldBe(1d);
        reports[0].TotalFiles.ShouldBe(121);
    }

    /// <summary>
    /// Le cœur du défaut : le travail doit quitter le thread appelant. Un <c>await</c> sur un appel
    /// synchrone ne déporte rien — il rend la main sur un travail déjà fait, l'interface ayant gelé
    /// entre-temps.
    /// </summary>
    [Fact]
    public async Task RemoveAsync_LeavesTheCallingThreadFree_WhileTheRecursiveDeleteRuns()
    {
        var mock = Installed(4);
        var callerThreadId = Environment.CurrentManagedThreadId;

        var directory = Substitute.For<IDirectory>();
        directory.Exists(Arg.Any<string>()).Returns(call => mock.Directory.Exists(call.Arg<string>()));
        directory.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
            .Returns(call => mock.Directory.GetFiles(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<SearchOption>(2)));
        directory.When(candidate => candidate.Delete(Arg.Any<string>(), Arg.Any<bool>())).Do(call =>
        {
            _deleteThreadId = Environment.CurrentManagedThreadId;
            _entered.Set();
            _gate.Wait();
            mock.Directory.Delete(call.ArgAt<string>(0), call.ArgAt<bool>(1));
        });

        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Directory.Returns(directory);
        fileSystem.File.Returns(mock.File);
        fileSystem.Path.Returns(mock.Path);
        fileSystem.FileInfo.Returns(mock.FileInfo);

        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var removal = repository.RemoveAsync(Version, cancellationToken: CancellationToken.None);

        _entered.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("la suppression n'a jamais démarré");
        removal.IsCompleted.ShouldBeFalse();
        _deleteThreadId.ShouldNotBe(callerThreadId);

        _gate.Set();
        await removal;

        mock.Directory.Exists(repository.GetVersionDirectory(Version)).ShouldBeFalse();
    }

    /// <summary>Une version qui n'est pas là n'est pas une erreur : il n'y a rien à faire.</summary>
    [Fact]
    public async Task RemoveAsync_OnAVersionThatIsNotInstalled_DoesNothingAndSaysItIsDone()
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstalledGameVersionRepository(fileSystem, Paths);
        var reports = new List<DirectoryDeleteProgress>();

        await repository.RemoveAsync(Version, new SynchronousProgress<DirectoryDeleteProgress>(reports.Add), CancellationToken.None);

        reports.ShouldHaveSingleItem().Ratio.ShouldBe(1d);
    }
}