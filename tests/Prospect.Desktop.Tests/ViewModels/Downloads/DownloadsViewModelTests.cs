using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Http;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Downloads;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Downloads;

/// <summary>
/// Le popover Téléchargements est une vue sur la file du <see cref="DownloadManager"/> réel : ces
/// tests le pilotent à travers un gestionnaire HTTP factice et un système de fichiers en mémoire,
/// pour vérifier que ce que voit l'utilisateur suit vraiment l'état du Core.
/// </summary>
public class DownloadsViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly Uri FileUrl = new("https://cdn.example/vs_client_linux-x64_1.22.6.tar.gz");

    private static DownloadManager CreateManager(HttpMessageHandler handler, MockFileSystem fileSystem)
        => new(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            fileSystem,
            Paths,
            new FakeClock(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero)),
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask),
            DownloadOptions.Default with { BufferSize = 64, ProgressStepBytes = 64 });

    private static DownloadRequest Request() => new("Vintage Story 1.22.6", "vs_client_linux-x64_1.22.6.tar.gz", [FileUrl]);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue("La condition attendue n'est jamais survenue.");
    }

    [Fact]
    public void EmptyQueue_ShowsNothing()
    {
        using var handler = new GatedBinaryHandler([1, 2, 3]);
        using var manager = CreateManager(handler, new MockFileSystem());
        using var viewModel = new DownloadsViewModel(manager, new ImmediateUiDispatcher());

        viewModel.Items.ShouldBeEmpty();
        viewModel.HasDownloads.ShouldBeFalse();
        viewModel.Count.ShouldBe(0);
        viewModel.SummaryText.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunningDownload_IsVisibleInTheQueueThenLeavesItOnSuccess()
    {
        using var handler = new GatedBinaryHandler(new byte[256], gated: true);
        using var manager = CreateManager(handler, new MockFileSystem());
        using var viewModel = new DownloadsViewModel(manager, new ImmediateUiDispatcher());

        var download = manager.DownloadAsync(Request(), cancellationToken: CancellationToken.None);
        await WaitUntilAsync(() => viewModel.Items.Count == 1);

        viewModel.HasDownloads.ShouldBeTrue();
        viewModel.Items[0].Name.ShouldBe("Vintage Story 1.22.6");
        viewModel.SummaryText.ShouldNotBeEmpty();

        handler.Release();
        await download;

        viewModel.Items.ShouldBeEmpty();
        viewModel.HasDownloads.ShouldBeFalse();
    }

    [Fact]
    public async Task FailedDownload_StaysVisibleWithItsReasonUntilDismissed()
    {
        using var handler = new GatedBinaryHandler([], failure: new HttpRequestException("réseau coupé"));
        using var manager = CreateManager(handler, new MockFileSystem());
        using var viewModel = new DownloadsViewModel(manager, new ImmediateUiDispatcher());

        await Should.ThrowAsync<DownloadFailedException>(() => manager.DownloadAsync(Request(), cancellationToken: CancellationToken.None));

        var item = viewModel.Items.ShouldHaveSingleItem();
        item.IsFailed.ShouldBeTrue();
        item.SpeedText.ShouldNotBeEmpty();

        manager.Dismiss(item.Operation);

        viewModel.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelCommand_OnAnItem_StopsThatDownload()
    {
        using var handler = new GatedBinaryHandler(new byte[256], gated: true);
        using var manager = CreateManager(handler, new MockFileSystem());
        using var viewModel = new DownloadsViewModel(manager, new ImmediateUiDispatcher());

        var download = manager.DownloadAsync(Request(), cancellationToken: CancellationToken.None);
        await WaitUntilAsync(() => viewModel.Items.Count == 1);

        viewModel.Items[0].CancelCommand.Execute(null);
        handler.Release();

        await Should.ThrowAsync<OperationCanceledException>(() => download);
        viewModel.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dispose_StopsFollowingTheQueue()
    {
        using var handler = new GatedBinaryHandler([], failure: new HttpRequestException("réseau coupé"));
        using var manager = CreateManager(handler, new MockFileSystem());
        var viewModel = new DownloadsViewModel(manager, new ImmediateUiDispatcher());
        viewModel.Dispose();

        await Should.ThrowAsync<DownloadFailedException>(() => manager.DownloadAsync(Request(), cancellationToken: CancellationToken.None));

        viewModel.Items.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        using var handler = new GatedBinaryHandler([]);
        using var manager = CreateManager(handler, new MockFileSystem());

        Should.Throw<ArgumentNullException>(() => new DownloadsViewModel(null!, new ImmediateUiDispatcher()));
        Should.Throw<ArgumentNullException>(() => new DownloadsViewModel(manager, null!));
    }
}