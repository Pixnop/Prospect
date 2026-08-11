namespace Prospect.Core.Tests.Http;

/// <summary>
/// Flux de test qui livre son contenu par petits blocs et peut simuler une coupure réseau en
/// pleine réception. Sert aussi de point d'accroche pour déclencher une annulation exactement au
/// milieu d'un transfert.
/// </summary>
internal sealed class ChunkedStream : Stream
{
    private readonly byte[] _data;
    private readonly int _chunkSize;
    private readonly int? _faultAfterBytes;
    private readonly Action<int>? _afterChunk;

    private int _position;

    public ChunkedStream(byte[] data, int chunkSize = 64, int? faultAfterBytes = null, Action<int>? afterChunk = null)
    {
        _data = data;
        _chunkSize = chunkSize;
        _faultAfterBytes = faultAfterBytes;
        _afterChunk = afterChunk;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _data.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_faultAfterBytes is { } limit && _position >= limit)
        {
            throw new IOException("La connexion a été perdue.");
        }

        var remaining = _data.Length - _position;
        if (remaining == 0)
        {
            return ValueTask.FromResult(0);
        }

        var count = Math.Min(Math.Min(_chunkSize, buffer.Length), remaining);
        if (_faultAfterBytes is { } cap)
        {
            count = Math.Min(count, cap - _position);
        }

        _data.AsSpan(_position, count).CopyTo(buffer.Span);
        _position += count;
        _afterChunk?.Invoke(_position);

        return ValueTask.FromResult(count);
    }

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}