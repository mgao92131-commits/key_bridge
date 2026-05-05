namespace BlueType.TestClient.Transports;

internal interface IAsyncDisposableStream : IAsyncDisposable
{
    Stream Stream { get; }
}
