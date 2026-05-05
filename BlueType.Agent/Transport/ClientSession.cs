using BlueType.Protocol;

namespace BlueType.Agent.Transport;

internal sealed class ClientSession : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _isDisposed;

    public ClientSession(Stream stream)
    {
        _stream = stream;
    }

    public Task<Envelope?> ReadAsync(CancellationToken cancellationToken = default)
    {
        return FrameCodec.ReadAsync(_stream, cancellationToken);
    }

    public async Task WriteAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
            await FrameCodec.WriteAsync(_stream, envelope, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        await _stream.DisposeAsync();
        _writeLock.Dispose();
    }
}
