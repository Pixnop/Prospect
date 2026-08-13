using System.IO.Abstractions;
using System.Net;
using System.Net.Http.Headers;

using Prospect.Core.Common;
using Prospect.Core.Storage;

namespace Prospect.Core.Http;

/// <summary>
/// Implémentation d'<see cref="IDownloadManager"/> : écriture en flux vers
/// <c>cache/downloads/&lt;fichier&gt;.partial</c>, reprise par en-tête <c>Range</c> après une
/// coupure (les deux miroirs officiels annoncent <c>Accept-Ranges: bytes</c>, vérifié en live par
/// la recherche), bascule automatique sur le miroir suivant, vérification MD5, puis renommage en
/// un seul mouvement.
/// </summary>
/// <remarks>
/// Le point de vigilance de ce composant est le temps : <see cref="HttpClient.Timeout"/> doit
/// valoir <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> sur le client injecté ici. Un
/// délai total par requête, celui qu'appliquent les politiques de résilience toutes faites,
/// tuerait mécaniquement un téléchargement de 600 Mo sur une ligne lente. Le garde-fou est
/// ailleurs : <see cref="DownloadOptions.ReadInactivityTimeout"/> coupe une connexion qui ne
/// livre plus rien, sans jamais borner la durée d'un transfert qui progresse.
/// </remarks>
public sealed class DownloadManager : IDownloadManager, IDisposable
{
    private const string PartialSuffix = ".partial";

    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fileSystem;
    private readonly AppPaths _appPaths;
    private readonly IClock _clock;
    private readonly RetryPolicy _retryPolicy;
    private readonly DownloadOptions _options;
    private readonly SemaphoreSlim _slots;
    private readonly List<DownloadOperation> _operations = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Construit le gestionnaire.
    /// </summary>
    /// <param name="httpClient">Client HTTP sans délai total (voir la remarque de la classe).</param>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    /// <param name="appPaths">Emplacement de <c>cache/downloads/</c>.</param>
    /// <param name="clock">Horloge, pour l'estimation de vitesse.</param>
    /// <param name="retryPolicy">Réessais par miroir. <see cref="RetryOptions.Default"/> si omise.</param>
    /// <param name="options">Réglages du moteur. <see cref="DownloadOptions.Default"/> si omis.</param>
    public DownloadManager(
        HttpClient httpClient,
        IFileSystem fileSystem,
        AppPaths appPaths,
        IClock clock,
        RetryPolicy? retryPolicy = null,
        DownloadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(clock);

        _httpClient = httpClient;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _clock = clock;
        _retryPolicy = retryPolicy ?? new RetryPolicy(RetryOptions.Default);
        _options = options ?? DownloadOptions.Default;
        _slots = new SemaphoreSlim(_options.MaxParallelDownloads, _options.MaxParallelDownloads);
    }

    /// <inheritdoc />
    public IReadOnlyList<DownloadOperation> Operations
    {
        get
        {
            lock (_gate)
            {
                return _operations.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? OperationsChanged;

    /// <inheritdoc />
    public async Task<string> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operation = new DownloadOperation(request.DisplayName, request.FileName, operationCancellation.Cancel);
        Add(operation);

        var token = operationCancellation.Token;
        try
        {
            await _slots.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Annulé avant même d'obtenir un créneau : rien n'a été écrit, mais la ligne reste,
            // barrée « annulé ». Un utilisateur qui annule a le droit de voir que c'est fait.
            operation.SetState(DownloadState.Canceled);
            Finish(operation);
            throw;
        }

        try
        {
            var path = await ExecuteAsync(request, operation, progress, token).ConfigureAwait(false);
            operation.SetState(DownloadState.Completed);
            Finish(operation);

            return path;
        }
        catch (OperationCanceledException)
        {
            DeleteQuietly(GetPartialPath(request.FileName));
            operation.SetState(DownloadState.Canceled);
            Finish(operation);
            throw;
        }
        catch (DownloadFailedException exception)
        {
            operation.Fail(exception.Message);
            Finish(operation);
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException or UnauthorizedAccessException)
        {
            var failure = DownloadFailedException.ForFile(request.FileName, exception);
            operation.Fail(failure.Message);
            Finish(operation);

            throw failure;
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <inheritdoc />
    public void Dismiss(DownloadOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Remove(operation);
    }

    /// <inheritdoc />
    public void DismissFinished()
    {
        var removed = false;
        lock (_gate)
        {
            removed = _operations.RemoveAll(candidate => candidate.IsFinished) > 0;
        }

        if (removed)
        {
            OperationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Libère le sémaphore qui borne le parallélisme.</summary>
    public void Dispose() => _slots.Dispose();

    private async Task<string> ExecuteAsync(
        DownloadRequest request,
        DownloadOperation operation,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        _fileSystem.Directory.CreateDirectory(_appPaths.DownloadsCacheDirectory);

        var targetPath = GetTargetPath(request.FileName);
        var partialPath = GetPartialPath(request.FileName);

        if (await TryReuseAsync(request, operation, progress, targetPath, cancellationToken).ConfigureAwait(false))
        {
            return targetPath;
        }

        var totalBytes = await ProbeTotalSizeAsync(request, cancellationToken).ConfigureAwait(false);
        SetPhase(operation, progress, DownloadState.Running);
        await ReceiveAsync(request, operation, progress, partialPath, totalBytes, cancellationToken).ConfigureAwait(false);
        await VerifyAsync(request, operation, progress, partialPath, cancellationToken).ConfigureAwait(false);

        _fileSystem.File.Move(partialPath, targetPath, overwrite: true);

        return targetPath;
    }

    // Un fichier déjà présent et conforme n'est pas retéléchargé : réinstaller une version qu'on
    // vient de supprimer ne doit pas coûter 600 Mo de réseau. S'il est présent mais faux, il est
    // jeté sans hésiter.
    private async Task<bool> TryReuseAsync(DownloadRequest request, DownloadOperation operation, IProgress<DownloadProgress>? progress, string targetPath, CancellationToken cancellationToken)
    {
        if (!_fileSystem.File.Exists(targetPath))
        {
            return false;
        }

        if (request.ExpectedMd5 is null)
        {
            return true;
        }

        SetPhase(operation, progress, DownloadState.Verifying);
        var actual = await Md5Checksum.ComputeAsync(_fileSystem, targetPath, _options.BufferSize, cancellationToken).ConfigureAwait(false);
        if (Md5Checksum.Matches(request.ExpectedMd5, actual))
        {
            return true;
        }

        DeleteQuietly(targetPath);

        return false;
    }

    private async Task<long?> ProbeTotalSizeAsync(DownloadRequest request, CancellationToken cancellationToken)
    {
        foreach (var mirror in request.Mirrors)
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Head, mirror);
                using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength is { } length)
                {
                    return length;
                }
            }
            catch (Exception exception) when (IsMirrorFailure(exception))
            {
                // Le HEAD n'est qu'un confort d'affichage : un miroir qui ne le supporte pas ne
                // doit pas empêcher le téléchargement, le GET donnera peut-être la taille.
            }
        }

        return null;
    }

    private async Task ReceiveAsync(
        DownloadRequest request,
        DownloadOperation operation,
        IProgress<DownloadProgress>? progress,
        string partialPath,
        long? totalBytes,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;

        foreach (var mirror in request.Mirrors)
        {
            try
            {
                await _retryPolicy.ExecuteAsync(
                    async (_, token) =>
                    {
                        await ReceiveFromMirrorAsync(mirror, operation, progress, partialPath, totalBytes, token).ConfigureAwait(false);

                        return true;
                    },
                    TransientHttpFailure.IsTransient,
                    cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (Exception exception) when (IsMirrorFailure(exception))
            {
                lastFailure = exception;
            }
        }

        throw DownloadFailedException.ForFile(request.FileName, lastFailure!);
    }

    private async Task ReceiveFromMirrorAsync(
        Uri mirror,
        DownloadOperation operation,
        IProgress<DownloadProgress>? progress,
        string partialPath,
        long? totalBytes,
        CancellationToken cancellationToken)
    {
        var alreadyReceived = GetPartialLength(partialPath);

        using var message = new HttpRequestMessage(HttpMethod.Get, mirror);
        if (alreadyReceived > 0)
        {
            message.Headers.Range = new RangeHeaderValue(alreadyReceived, null);
        }

        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (alreadyReceived > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // Le serveur refuse la plage demandée parce qu'il n'y a plus rien à envoyer : le
            // fichier partiel est en réalité complet, la vérification tranchera.
            return;
        }

        response.EnsureSuccessStatusCode();

        var resuming = alreadyReceived > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        var offset = resuming ? alreadyReceived : 0L;
        var declaredTotal = totalBytes ?? ResolveTotal(response, resuming);

        var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            var destination = resuming
                ? _fileSystem.File.Open(partialPath, FileMode.Append, FileAccess.Write)
                : _fileSystem.File.Create(partialPath);

            await using (destination.ConfigureAwait(false))
            {
                await CopyAsync(source, destination, operation, progress, offset, declaredTotal, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task CopyAsync(
        Stream source,
        Stream destination,
        DownloadOperation operation,
        IProgress<DownloadProgress>? progress,
        long alreadyReceived,
        long? totalBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[_options.BufferSize];
        var received = alreadyReceived;
        var lastReported = received;
        var speed = new DownloadSpeedEstimator(_clock);
        speed.Start(received);
        Report(operation, progress, received, totalBytes, 0d);

        while (true)
        {
            var read = await ReadAsync(source, buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;

            var bytesPerSecond = speed.Update(received);
            if (received - lastReported >= _options.ProgressStepBytes)
            {
                lastReported = received;
                Report(operation, progress, received, totalBytes, bytesPerSecond);
            }
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        Report(operation, progress, received, totalBytes ?? received, speed.Update(received));
    }

    // Le délai d'inactivité s'applique à chaque lecture, jamais à la durée totale du transfert.
    private async Task<int> ReadAsync(Stream source, byte[] buffer, CancellationToken cancellationToken)
    {
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readCancellation.CancelAfter(_options.ReadInactivityTimeout);

        try
        {
            return await source.ReadAsync(buffer, readCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Aucune donnée reçue depuis {_options.ReadInactivityTimeout.TotalSeconds:0} s.");
        }
    }

    private async Task VerifyAsync(DownloadRequest request, DownloadOperation operation, IProgress<DownloadProgress>? progress, string partialPath, CancellationToken cancellationToken)
    {
        if (request.ExpectedMd5 is null)
        {
            return;
        }

        SetPhase(operation, progress, DownloadState.Verifying);
        var actual = await Md5Checksum.ComputeAsync(_fileSystem, partialPath, _options.BufferSize, cancellationToken).ConfigureAwait(false);
        if (Md5Checksum.Matches(request.ExpectedMd5, actual))
        {
            return;
        }

        DeleteQuietly(partialPath);

        throw DownloadChecksumMismatchException.Create(request.FileName, request.ExpectedMd5, actual);
    }

    private static long? ResolveTotal(HttpResponseMessage response, bool resuming)
        => resuming ? response.Content.Headers.ContentRange?.Length : response.Content.Headers.ContentLength;

    private static bool IsMirrorFailure(Exception exception)
        => exception is HttpRequestException or IOException or TimeoutException;

    private static void Report(DownloadOperation operation, IProgress<DownloadProgress>? progress, long received, long? total, double bytesPerSecond)
    {
        var snapshot = new DownloadProgress(received, total, bytesPerSecond, operation.State);
        operation.SetProgress(snapshot);
        progress?.Report(snapshot);
    }

    // Un changement de phase est en soi une information d'avancement : il est publié comme telle,
    // sinon l'appelant ne saurait jamais qu'on est passé de « je télécharge » à « je vérifie ».
    private static void SetPhase(DownloadOperation operation, IProgress<DownloadProgress>? progress, DownloadState state)
    {
        operation.SetState(state);
        var snapshot = operation.Progress with { State = state };
        operation.SetProgress(snapshot);
        progress?.Report(snapshot);
    }

    private long GetPartialLength(string partialPath)
        => _fileSystem.File.Exists(partialPath) ? _fileSystem.FileInfo.New(partialPath).Length : 0L;

    private string GetTargetPath(string fileName) => _fileSystem.Path.Combine(_appPaths.DownloadsCacheDirectory, fileName);

    private string GetPartialPath(string fileName) => GetTargetPath(fileName) + PartialSuffix;

    private void DeleteQuietly(string path)
    {
        try
        {
            if (_fileSystem.File.Exists(path))
            {
                _fileSystem.File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Un fichier temporaire qu'on n'arrive pas à supprimer ne doit pas masquer l'erreur
            // qui a mené jusqu'ici : il sera écrasé ou repris à la prochaine tentative.
        }
    }

    private void Add(DownloadOperation operation)
    {
        lock (_gate)
        {
            _operations.Add(operation);
        }

        OperationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Remove(DownloadOperation operation)
    {
        bool removed;
        lock (_gate)
        {
            removed = _operations.Remove(operation);
        }

        if (removed)
        {
            OperationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Marque la fin d'une opération et taille l'historique. La ligne RESTE dans la file : c'est
    /// tout l'intérêt, un téléchargement abouti doit pouvoir se relire après coup. Ce qui la fait
    /// partir, c'est un retrait explicite ou l'arrivée d'opérations plus récentes.
    /// </summary>
    private void Finish(DownloadOperation operation)
    {
        operation.MarkFinished(_clock.UtcNow);
        Trim();
        OperationsChanged?.Invoke(this, EventArgs.Empty);
    }

    // Les opérations VIVANTES ne sont jamais évincées, quel que soit leur nombre : une file de
    // trente téléchargements en cours reste affichée en entier. Seul l'historique est borné, et il
    // perd toujours ses lignes les plus anciennes.
    private void Trim()
    {
        lock (_gate)
        {
            var excess = _operations.Count(candidate => candidate.IsFinished) - _options.HistoryLimit;
            for (var index = 0; index < _operations.Count && excess > 0; index++)
            {
                if (!_operations[index].IsFinished)
                {
                    continue;
                }

                _operations.RemoveAt(index);
                index--;
                excess--;
            }
        }
    }
}