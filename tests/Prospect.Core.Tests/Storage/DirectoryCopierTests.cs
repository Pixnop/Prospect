using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Storage;
using Prospect.Core.Tests.Instances;

using Shouldly;

namespace Prospect.Core.Tests.Storage;

public class DirectoryCopierTests
{
    [Fact]
    public void Constructor_NullFileSystem_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new DirectoryCopier(null!));
    }

    [Fact]
    public async Task CopyAsync_CopiesAllFilesPreservingContentAndRelativeStructure()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/source/a.txt", new MockFileData("a"));
        fileSystem.AddFile("/source/Mods/carrycapacity.zip", new MockFileData("mod-content"));
        var copier = new DirectoryCopier(fileSystem);

        await copier.CopyAsync("/source", "/target", progress: null, CancellationToken.None);

        fileSystem.File.ReadAllText("/target/a.txt").ShouldBe("a");
        fileSystem.File.ReadAllText(fileSystem.Path.Combine("/target", "Mods", "carrycapacity.zip")).ShouldBe("mod-content");
    }

    [Fact]
    public async Task CopyAsync_SourceMissing_StillCreatesEmptyTargetDirectory()
    {
        var fileSystem = new MockFileSystem();
        var copier = new DirectoryCopier(fileSystem);

        await copier.CopyAsync("/source", "/target", progress: null, CancellationToken.None);

        fileSystem.Directory.Exists("/target").ShouldBeTrue();
    }

    [Fact]
    public async Task CopyAsync_ReportsProgressPerFile()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/source/a.txt", new MockFileData("a"));
        fileSystem.AddFile("/source/b.txt", new MockFileData("b"));
        var copier = new DirectoryCopier(fileSystem);
        var reports = new List<DirectoryCopyProgress>();

        await copier.CopyAsync("/source", "/target", new SynchronousProgress<DirectoryCopyProgress>(reports.Add), CancellationToken.None);

        reports.Count.ShouldBe(2);
        reports.ShouldAllBe(r => r.TotalFiles == 2);
        reports.Select(r => r.FilesCopied).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task CopyAsync_CancelledBeforeStart_ThrowsWithoutCreatingTargetDirectory()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/source/a.txt", new MockFileData("a"));
        var copier = new DirectoryCopier(fileSystem);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => copier.CopyAsync("/source", "/target", null, cts.Token));

        fileSystem.Directory.Exists("/target").ShouldBeFalse();
    }

    [Fact]
    public async Task CopyAsync_CancelledDuringCopy_StopsAfterTheCurrentFile()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/source/a.txt", new MockFileData("a"));
        fileSystem.AddFile("/source/b.txt", new MockFileData("b"));
        fileSystem.AddFile("/source/c.txt", new MockFileData("c"));
        var copier = new DirectoryCopier(fileSystem);
        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<DirectoryCopyProgress>(p =>
        {
            if (p.FilesCopied == 1)
            {
                cts.Cancel();
            }
        });

        await Should.ThrowAsync<OperationCanceledException>(() => copier.CopyAsync("/source", "/target", progress, cts.Token));

        fileSystem.Directory.GetFiles("/target", "*", SearchOption.AllDirectories).Length.ShouldBe(1);
    }

    [Fact]
    public async Task CopyAsync_NullOrEmptySourceDirectory_ThrowsArgumentException()
    {
        var fileSystem = new MockFileSystem();
        var copier = new DirectoryCopier(fileSystem);

        await Should.ThrowAsync<ArgumentException>(() => copier.CopyAsync(string.Empty, "/target", null, CancellationToken.None));
    }

    [Fact]
    public async Task CopyAsync_NullOrEmptyTargetDirectory_ThrowsArgumentException()
    {
        var fileSystem = new MockFileSystem();
        var copier = new DirectoryCopier(fileSystem);

        await Should.ThrowAsync<ArgumentException>(() => copier.CopyAsync("/source", string.Empty, null, CancellationToken.None));
    }
}