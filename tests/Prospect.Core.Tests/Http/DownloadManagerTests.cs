using System.IO.Abstractions.TestingHelpers;
using System.Net;

using Prospect.Core.Http;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Instances;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Http;

public sealed class DownloadManagerTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri CdnUrl = new("https://cdn.example/vs_client_linux-x64_1.22.6.tar.gz");
    private static readonly Uri LocalUrl = new("https://local.example/vs_client_linux-x64_1.22.6.tar.gz");

    private static byte[] Payload(int length = 1000)
    {
        var content = new byte[length];
        for (var index = 0; index < length; index++)
        {
            content[index] = (byte)(index % 251);
        }

        return content;
    }

    private static DownloadManager CreateManager(FakeHttpMessageHandler handler, MockFileSystem fileSystem, DownloadOptions? options = null)
        => new(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            fileSystem,
            Paths,
            new FakeClock(Noon),
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask),
            options ?? DownloadOptions.Default with { BufferSize = 128, ProgressStepBytes = 128 });

    private static DownloadRequest Request(FakeDownloadServer server, params Uri[] mirrors)
        => new("Vintage Story 1.22.6", "vs_client_linux-x64_1.22.6.tar.gz", mirrors, server.Md5);

    private static string TargetPath(MockFileSystem fileSystem, DownloadRequest request)
        => fileSystem.Path.Combine(Paths.DownloadsCacheDirectory, request.FileName);

    [Fact]
    public async Task DownloadAsync_HappyPath_WritesTheCompleteFileAndLeavesNoPartial()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var manager = CreateManager(handler, fileSystem);
        var request = Request(server, CdnUrl);

        var path = await manager.DownloadAsync(request, cancellationToken: CancellationToken.None);

        path.ShouldBe(TargetPath(fileSystem, request));
        fileSystem.File.ReadAllBytes(path).ShouldBe(server.Content);
        fileSystem.File.Exists(path + ".partial").ShouldBeFalse();
    }

    [Fact]
    public async Task DownloadAsync_AsksTheTotalSizeWithAHeadRequestBeforeStreaming()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        using var manager = CreateManager(handler, new MockFileSystem());

        await manager.DownloadAsync(Request(server, CdnUrl), cancellationToken: CancellationToken.None);

        handler.Requests[0].Method.ShouldBe(HttpMethod.Head);
        handler.Requests[1].Method.ShouldBe(HttpMethod.Get);
    }

    [Fact]
    public async Task DownloadAsync_ReportsProgressWithTheByteCountFromContentLength()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        using var manager = CreateManager(handler, new MockFileSystem());
        var reports = new List<DownloadProgress>();

        await manager.DownloadAsync(
            Request(server, CdnUrl),
            new SynchronousProgress<DownloadProgress>(reports.Add),
            CancellationToken.None);

        reports.ShouldNotBeEmpty();
        reports[^1].ReceivedBytes.ShouldBe(1000);
        reports[^1].TotalBytes.ShouldBe(1000);
        reports[^1].Ratio.ShouldBe(1d);
        reports.Where(report => report.ReceivedBytes > 0).ShouldAllBe(report => report.TotalBytes == 1000);
        reports.Select(report => report.State).ShouldContain(DownloadState.Verifying);
    }

    [Fact]
    public async Task DownloadAsync_ChecksumMismatch_ThrowsTheTypedFailureAndDeletesThePartialFile()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var manager = CreateManager(handler, fileSystem);
        var request = Request(server, CdnUrl) with { ExpectedMd5 = "00000000000000000000000000000000" };

        var exception = await Should.ThrowAsync<DownloadChecksumMismatchException>(
            () => manager.DownloadAsync(request, cancellationToken: CancellationToken.None));

        exception.ExpectedMd5.ShouldBe("00000000000000000000000000000000");
        exception.ActualMd5.ShouldBe(server.Md5);
        fileSystem.File.Exists(TargetPath(fileSystem, request) + ".partial").ShouldBeFalse();
        fileSystem.File.Exists(TargetPath(fileSystem, request)).ShouldBeFalse();
    }

    [Fact]
    public async Task DownloadAsync_ChecksumMismatch_LeavesTheFailureVisibleInTheQueue()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        using var manager = CreateManager(handler, new MockFileSystem());
        var request = Request(server, CdnUrl) with { ExpectedMd5 = "00000000000000000000000000000000" };

        await Should.ThrowAsync<DownloadChecksumMismatchException>(
            () => manager.DownloadAsync(request, cancellationToken: CancellationToken.None));

        var failed = manager.Operations.ShouldHaveSingleItem();
        failed.State.ShouldBe(DownloadState.Failed);
        failed.FailureMessage.ShouldNotBeNull();

        manager.Dismiss(failed);
        manager.Operations.ShouldBeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_CdnUnreachable_FallsBackToTheSecondMirror()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(request => request.RequestUri == CdnUrl
            ? throw new HttpRequestException("le CDN ne répond pas")
            : server.Handle(request));
        var fileSystem = new MockFileSystem();
        using var manager = CreateManager(handler, fileSystem);
        var request = Request(server, CdnUrl, LocalUrl);

        var path = await manager.DownloadAsync(request, cancellationToken: CancellationToken.None);

        fileSystem.File.ReadAllBytes(path).ShouldBe(server.Content);
        handler.Requests.ShouldContain(recorded => recorded.Url == LocalUrl && recorded.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task DownloadAsync_ServerErrorOnTheCdn_FallsBackToTheSecondMirror()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(request => request.RequestUri == CdnUrl
            ? FakeHttpMessageHandler.Status(HttpStatusCode.ServiceUnavailable)
            : server.Handle(request));
        var fileSystem = new MockFileSystem();
        using var manager = CreateManager(handler, fileSystem);

        var path = await manager.DownloadAsync(Request(server, CdnUrl, LocalUrl), cancellationToken: CancellationToken.None);

        fileSystem.File.ReadAllBytes(path).ShouldBe(server.Content);
    }

    [Fact]
    public async Task DownloadAsync_EveryMirrorDown_ThrowsTheTypedFailure()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("réseau coupé"));
        using var manager = CreateManager(handler, new MockFileSystem());

        var exception = await Should.ThrowAsync<DownloadFailedException>(
            () => manager.DownloadAsync(Request(server, CdnUrl, LocalUrl), cancellationToken: CancellationToken.None));

        exception.InnerException.ShouldBeOfType<HttpRequestException>();
        manager.Operations.ShouldHaveSingleItem().State.ShouldBe(DownloadState.Failed);
    }

    [Fact]
    public async Task DownloadAsync_ConnectionDropsMidStream_ResumesWithARangeRequest()
    {
        var server = new FakeDownloadServer(Payload()) { FaultPlan = [400] };
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var manager = CreateManager(handler, fileSystem);

        var path = await manager.DownloadAsync(Request(server, CdnUrl), cancellationToken: CancellationToken.None);

        fileSystem.File.ReadAllBytes(path).ShouldBe(server.Content);
        server.GetCount.ShouldBe(2);
        handler.Requests.Last(recorded => recorded.Method == HttpMethod.Get).RangeHeader.ShouldBe("bytes=400-");
    }

    [Fact]
    public async Task DownloadAsync_ServerIgnoresTheRangeHeader_RestartsFromTheBeginning()
    {
        var server = new FakeDownloadServer(Payload()) { FaultPlan = [400], SupportsRange = false };
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var manager = CreateManager(handler, fileSystem);

        var path = await manager.DownloadAsync(Request(server, CdnUrl), cancellationToken: CancellationToken.None);

        fileSystem.File.ReadAllBytes(path).ShouldBe(server.Content);
    }

    [Fact]
    public async Task DownloadAsync_ServerRefusesTheRangeBecauseNothingIsLeft_AcceptsThePartialAsComplete()
    {
        var payload = Payload();
        var server = new FakeDownloadServer(payload) { RejectRange = true };
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var manager = CreateManager(handler, fileSystem);
        var request = Request(server, CdnUrl);
        fileSystem.AddFile(TargetPath(fileSystem, request) + ".partial", new MockFileData(payload));

        var path = await manager.DownloadAsync(request, cancellationToken: CancellationToken.None);

        fileSystem.File.ReadAllBytes(path).ShouldBe(payload);
    }

    [Fact]
    public async Task DownloadAsync_CanceledMidStream_DeletesThePartialFileAndLeavesACanceledRow()
    {
        using var cancellation = new CancellationTokenSource();
        var server = new FakeDownloadServer(Payload()) { AfterChunk = sent => { if (sent >= 128) cancellation.Cancel(); } };
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var manager = CreateManager(handler, fileSystem);
        var request = Request(server, CdnUrl);

        await Should.ThrowAsync<OperationCanceledException>(
            () => manager.DownloadAsync(request, cancellationToken: cancellation.Token));

        fileSystem.File.Exists(TargetPath(fileSystem, request) + ".partial").ShouldBeFalse();
        fileSystem.File.Exists(TargetPath(fileSystem, request)).ShouldBeFalse();

        // La ligne reste, barrée « annulé » : ce qu'un utilisateur vient d'interrompre doit se
        // relire, et c'est lui qui la retire.
        var canceled = manager.Operations.ShouldHaveSingleItem();
        canceled.State.ShouldBe(DownloadState.Canceled);
        canceled.FinishedUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task DownloadAsync_CanceledThroughTheOperationHandle_StopsThatDownload()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        using var manager = CreateManager(handler, new MockFileSystem());
        DownloadOperation? started = null;
        manager.OperationsChanged += (_, _) => started ??= manager.Operations is [var first, ..] ? first : null;
        server.AfterChunk = sent =>
        {
            if (sent >= 128)
            {
                started?.Cancel();
            }
        };

        await Should.ThrowAsync<OperationCanceledException>(
            () => manager.DownloadAsync(Request(server, CdnUrl), cancellationToken: CancellationToken.None));

        manager.Operations.ShouldHaveSingleItem().State.ShouldBe(DownloadState.Canceled);
    }

    [Fact]
    public async Task DownloadAsync_FileAlreadyDownloadedAndValid_SkipsTheNetworkEntirely()
    {
        var payload = Payload();
        var server = new FakeDownloadServer(payload);
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var manager = CreateManager(handler, fileSystem);
        var request = Request(server, CdnUrl);
        fileSystem.AddFile(TargetPath(fileSystem, request), new MockFileData(payload));

        var path = await manager.DownloadAsync(request, cancellationToken: CancellationToken.None);

        path.ShouldBe(TargetPath(fileSystem, request));
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_FileAlreadyDownloadedButCorrupted_DownloadsItAgain()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var manager = CreateManager(handler, fileSystem);
        var request = Request(server, CdnUrl);
        fileSystem.AddFile(TargetPath(fileSystem, request), new MockFileData([1, 2, 3]));

        var path = await manager.DownloadAsync(request, cancellationToken: CancellationToken.None);

        fileSystem.File.ReadAllBytes(path).ShouldBe(server.Content);
        server.GetCount.ShouldBe(1);
    }

    [Fact]
    public async Task DownloadAsync_ServerWithoutHeadSupport_StillDownloadsWithTheGetContentLength()
    {
        var server = new FakeDownloadServer(Payload()) { SupportsHead = false };
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var manager = CreateManager(handler, fileSystem);
        var reports = new List<DownloadProgress>();

        var path = await manager.DownloadAsync(
            Request(server, CdnUrl),
            new SynchronousProgress<DownloadProgress>(reports.Add),
            CancellationToken.None);

        fileSystem.File.ReadAllBytes(path).ShouldBe(server.Content);
        reports[^1].TotalBytes.ShouldBe(1000);
    }

    [Fact]
    public async Task DownloadAsync_WhileRunning_ExposesTheOperationInTheQueueThenArchivesIt()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        using var manager = CreateManager(handler, new MockFileSystem());
        var changes = 0;
        var seenStates = new List<DownloadState>();
        manager.OperationsChanged += (_, _) =>
        {
            changes++;
            foreach (var operation in manager.Operations)
            {
                operation.Changed += (sender, _) => seenStates.Add(((DownloadOperation)sender!).State);
            }
        };

        await manager.DownloadAsync(Request(server, CdnUrl), cancellationToken: CancellationToken.None);

        // Deux notifications de composition : l'entrée dans la file, puis le passage à l'état
        // terminal. La ligne ne sort pas, elle devient de l'historique.
        changes.ShouldBe(2);
        seenStates.ShouldContain(DownloadState.Running);
        seenStates.ShouldContain(DownloadState.Verifying);
        seenStates.ShouldContain(DownloadState.Completed);

        var archived = manager.Operations.ShouldHaveSingleItem();
        archived.State.ShouldBe(DownloadState.Completed);
        archived.IsFinished.ShouldBeTrue();
        archived.FinishedUtc.ShouldBe(Noon);
    }

    /// <summary>
    /// L'historique est borné et perd ses lignes les plus anciennes ; ce qui tourne encore n'est
    /// jamais évincé, quel que soit le nombre de lignes terminées qui s'accumulent.
    /// </summary>
    [Fact]
    public async Task Operations_BeyondTheHistoryLimit_DropTheOldestFinishedRows()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        using var manager = CreateManager(handler, new MockFileSystem(), DownloadOptions.Default with { HistoryLimit = 2 });

        for (var index = 0; index < 4; index++)
        {
            await manager.DownloadAsync(
                Request(server, CdnUrl) with { FileName = $"fichier-{index}.tar.gz" },
                cancellationToken: CancellationToken.None);
        }

        manager.Operations.Select(operation => operation.FileName)
            .ShouldBe(["fichier-2.tar.gz", "fichier-3.tar.gz"]);
    }

    [Fact]
    public async Task DismissFinished_ClearsTheHistoryAndKeepsWhatIsStillRunning()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        using var manager = CreateManager(handler, new MockFileSystem());

        await manager.DownloadAsync(Request(server, CdnUrl), cancellationToken: CancellationToken.None);
        manager.Operations.ShouldHaveSingleItem();

        manager.DismissFinished();

        manager.Operations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("../evil.sh")]
    [InlineData("sub/dir/file.tar.gz")]
    [InlineData("")]
    public async Task DownloadAsync_FileNameThatEscapesTheCache_IsRejected(string fileName)
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        using var manager = CreateManager(handler, new MockFileSystem());

        await Should.ThrowAsync<ArgumentException>(() => manager.DownloadAsync(
            new DownloadRequest("Piégé", fileName, [CdnUrl]),
            cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_WithoutAnyMirror_IsRejected()
    {
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Status(HttpStatusCode.OK));
        using var manager = CreateManager(handler, new MockFileSystem());

        await Should.ThrowAsync<ArgumentException>(() => manager.DownloadAsync(
            new DownloadRequest("Sans miroir", "file.bin", []),
            cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_WithoutExpectedChecksum_SkipsVerification()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        var fileSystem = new MockFileSystem();
        using var manager = CreateManager(handler, fileSystem);

        var path = await manager.DownloadAsync(
            new DownloadRequest("Sans empreinte", "file.bin", [CdnUrl]),
            cancellationToken: CancellationToken.None);

        fileSystem.File.ReadAllBytes(path).ShouldBe(server.Content);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var server = new FakeDownloadServer(Payload());
        using var handler = new FakeHttpMessageHandler(server.Handle);
        using var client = new HttpClient(handler);
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Noon);

        Should.Throw<ArgumentNullException>(() => new DownloadManager(null!, fileSystem, Paths, clock));
        Should.Throw<ArgumentNullException>(() => new DownloadManager(client, null!, Paths, clock));
        Should.Throw<ArgumentNullException>(() => new DownloadManager(client, fileSystem, null!, clock));
        Should.Throw<ArgumentNullException>(() => new DownloadManager(client, fileSystem, Paths, null!));
    }
}