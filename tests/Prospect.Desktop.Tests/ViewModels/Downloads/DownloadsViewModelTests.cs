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
///
/// Depuis que la file garde ce qui est terminé, ce panneau est aussi l'historique de la session :
/// ces tests couvrent donc les deux moitiés, ce qui tourne et ce qui a eu lieu.
/// </summary>
public class DownloadsViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly Uri FileUrl = new("https://cdn.example/vs_client_linux-x64_1.22.6.tar.gz");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private static DownloadManager CreateManager(HttpMessageHandler handler, MockFileSystem fileSystem, FakeClock? clock = null, int historyLimit = 20)
        => new(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            fileSystem,
            Paths,
            clock ?? new FakeClock(Noon),
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask),
            DownloadOptions.Default with { BufferSize = 64, ProgressStepBytes = 64, HistoryLimit = historyLimit });

    private static DownloadsViewModel CreateViewModel(IDownloadManager manager, FakeClock? clock = null)
        => new(manager, new ImmediateUiDispatcher(), clock ?? new FakeClock(Noon));

    private static DownloadRequest Request(string fileName = "vs_client_linux-x64_1.22.6.tar.gz")
        => new("Vintage Story 1.22.6", fileName, [FileUrl]);

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
        using var viewModel = CreateViewModel(manager);

        viewModel.Items.ShouldBeEmpty();
        viewModel.HasDownloads.ShouldBeFalse();
        viewModel.HasFinished.ShouldBeFalse();
        viewModel.Count.ShouldBe(0);
        viewModel.SummaryText.ShouldBeEmpty();
    }

    /// <summary>
    /// Le défaut corrigé : un téléchargement réussi disparaissait à la seconde où il aboutissait, et
    /// plus rien ne disait qu'il avait eu lieu. Il reste, avec son verdict et sa taille.
    /// </summary>
    [Fact]
    public async Task SuccessfulDownload_StaysInTheListAsAHistoryRow()
    {
        using var handler = new GatedBinaryHandler(new byte[256], gated: true);
        using var manager = CreateManager(handler, new MockFileSystem());
        using var viewModel = CreateViewModel(manager);

        var download = manager.DownloadAsync(Request(), cancellationToken: CancellationToken.None);
        await WaitUntilAsync(() => viewModel.Items.Count == 1);

        viewModel.HasDownloads.ShouldBeTrue();
        viewModel.Items[0].Name.ShouldBe("Vintage Story 1.22.6");
        viewModel.Items[0].IsFinished.ShouldBeFalse();
        viewModel.SummaryText.ShouldNotBeEmpty();

        handler.Release();
        await download;

        var item = viewModel.Items.ShouldHaveSingleItem();
        item.IsFinished.ShouldBeTrue();
        item.OutcomeText.ShouldBe("terminé");
        item.OutcomeTone.ShouldBe("stable");
        item.StatText.ShouldBe("256 B");
        item.FinishedText.ShouldBe("à l'instant");

        // La pastille de la barre latérale compte ce qui TOURNE, pas ce qui est archivé.
        viewModel.Count.ShouldBe(0);
        viewModel.HasFinished.ShouldBeTrue();
    }

    [Fact]
    public async Task FailedDownload_StaysVisibleWithItsReasonUntilDismissed()
    {
        using var handler = new GatedBinaryHandler([], failure: new HttpRequestException("réseau coupé"));
        using var manager = CreateManager(handler, new MockFileSystem());
        using var viewModel = CreateViewModel(manager);

        await Should.ThrowAsync<DownloadFailedException>(() => manager.DownloadAsync(Request(), cancellationToken: CancellationToken.None));

        var item = viewModel.Items.ShouldHaveSingleItem();
        item.IsFailed.ShouldBeTrue();
        item.IsFinished.ShouldBeTrue();
        item.OutcomeText.ShouldBe("échec");
        item.SpeedText.ShouldNotBeEmpty();

        item.DismissCommand.Execute(null);

        viewModel.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelingARunningDownload_LeavesACanceledRowBehind()
    {
        using var handler = new GatedBinaryHandler(new byte[256], gated: true);
        using var manager = CreateManager(handler, new MockFileSystem());
        using var viewModel = CreateViewModel(manager);

        var download = manager.DownloadAsync(Request(), cancellationToken: CancellationToken.None);
        await WaitUntilAsync(() => viewModel.Items.Count == 1);

        // Sur une ligne vivante, la croix annule.
        viewModel.Items[0].DismissCommand.Execute(null);
        handler.Release();

        await Should.ThrowAsync<OperationCanceledException>(() => download);

        var item = viewModel.Items.ShouldHaveSingleItem();
        item.OutcomeText.ShouldBe("annulé");
        item.IsFinished.ShouldBeTrue();

        // Sur la même ligne devenue historique, elle retire.
        item.DismissCommand.Execute(null);
        viewModel.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ClearAll_RemovesTheHistoryAndKeepsWhatIsStillRunning()
    {
        using var handler = new GatedBinaryHandler(new byte[64], gated: true);
        using var manager = CreateManager(handler, new MockFileSystem());
        using var viewModel = CreateViewModel(manager);

        using var abandoned = new CancellationTokenSource();
        var canceled = manager.DownloadAsync(Request("abandonné.zip"), cancellationToken: abandoned.Token);
        await WaitUntilAsync(() => viewModel.Items.Count == 1);
        await abandoned.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => canceled);

        var running = manager.DownloadAsync(Request(), cancellationToken: CancellationToken.None);
        await WaitUntilAsync(() => viewModel.Items.Count == 2);

        viewModel.ClearFinishedCommand.Execute(null);

        var item = viewModel.Items.ShouldHaveSingleItem();
        item.IsFinished.ShouldBeFalse();

        handler.Release();
        await running;
    }

    /// <summary>
    /// L'historique est borné : au-delà de la limite, les plus anciennes lignes sortent seules.
    /// Sans cela, une session de rattrapage de modpack laisserait cent lignes dans le panneau.
    /// </summary>
    [Fact]
    public async Task TheHistory_IsBoundedAndDropsItsOldestRowsFirst()
    {
        var clock = new FakeClock(Noon);
        using var handler = new GatedBinaryHandler([], failure: new HttpRequestException("réseau coupé"));
        using var manager = CreateManager(handler, new MockFileSystem(), clock, historyLimit: 3);
        using var viewModel = CreateViewModel(manager, clock);

        for (var index = 0; index < 5; index++)
        {
            clock.UtcNow = Noon.AddMinutes(index);
            await Should.ThrowAsync<DownloadFailedException>(() => manager.DownloadAsync(
                new DownloadRequest($"Mod {index}", $"mod-{index}.zip", [new Uri($"https://cdn.example/mod-{index}.zip")]),
                cancellationToken: CancellationToken.None));
        }

        viewModel.Items.Count.ShouldBe(3);
        viewModel.Items.Select(item => item.Name).ShouldBe(["Mod 4", "Mod 3", "Mod 2"]);
    }

    /// <summary>Ce qui tourne reste en haut : le vivant d'abord, l'historique du plus récent au plus ancien.</summary>
    [Fact]
    public async Task RunningRowsStayOnTop_AndTheHistoryReadsNewestFirst()
    {
        var clock = new FakeClock(Noon);
        using var handler = new GatedBinaryHandler(new byte[64], gated: true);
        using var manager = CreateManager(handler, new MockFileSystem(), clock);
        using var viewModel = CreateViewModel(manager, clock);

        foreach (var (name, minutes) in new[] { ("Ancien", 0), ("Récent", 5) })
        {
            clock.UtcNow = Noon.AddMinutes(minutes);
            using var cancellation = new CancellationTokenSource();
            var abandoned = manager.DownloadAsync(
                new DownloadRequest(name, $"{minutes}.zip", [new Uri($"https://cdn.example/{minutes}.zip")]),
                cancellationToken: cancellation.Token);
            await cancellation.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => abandoned);
        }

        var running = manager.DownloadAsync(Request(), cancellationToken: CancellationToken.None);
        await WaitUntilAsync(() => viewModel.Items.Count == 3);

        viewModel.Items[0].IsFinished.ShouldBeFalse();
        viewModel.Items[1].Name.ShouldBe("Récent");
        viewModel.Items[2].Name.ShouldBe("Ancien");

        handler.Release();
        await running;
    }

    /// <summary>
    /// L'heure relative se reprend à la demande plutôt que sur un minuteur : c'est ce que fait le
    /// shell quand l'utilisateur ouvre le popover.
    /// </summary>
    [Fact]
    public async Task RefreshElapsed_RewritesTheRelativeTimeOfEveryHistoryRow()
    {
        var clock = new FakeClock(Noon);
        using var handler = new GatedBinaryHandler([], failure: new HttpRequestException("réseau coupé"));
        using var manager = CreateManager(handler, new MockFileSystem(), clock);
        using var viewModel = CreateViewModel(manager, clock);

        await Should.ThrowAsync<DownloadFailedException>(() => manager.DownloadAsync(Request(), cancellationToken: CancellationToken.None));
        viewModel.Items[0].FinishedText.ShouldBe("à l'instant");

        clock.UtcNow = Noon.AddMinutes(3);
        viewModel.RefreshElapsed();

        viewModel.Items[0].FinishedText.ShouldBe("il y a 3 minutes");
    }

    [Fact]
    public async Task Dispose_StopsFollowingTheQueue()
    {
        using var handler = new GatedBinaryHandler([], failure: new HttpRequestException("réseau coupé"));
        using var manager = CreateManager(handler, new MockFileSystem());
        var viewModel = CreateViewModel(manager);
        viewModel.Dispose();

        await Should.ThrowAsync<DownloadFailedException>(() => manager.DownloadAsync(Request(), cancellationToken: CancellationToken.None));

        viewModel.Items.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        using var handler = new GatedBinaryHandler([]);
        using var manager = CreateManager(handler, new MockFileSystem());

        Should.Throw<ArgumentNullException>(() => new DownloadsViewModel(null!, new ImmediateUiDispatcher(), new FakeClock(Noon)));
        Should.Throw<ArgumentNullException>(() => new DownloadsViewModel(manager, null!, new FakeClock(Noon)));
        Should.Throw<ArgumentNullException>(() => new DownloadsViewModel(manager, new ImmediateUiDispatcher(), null!));
    }
}