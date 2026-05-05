using System.Net.Sockets;

namespace BlueType.TestClient.Transports;

internal sealed class TcpTransport : IAsyncDisposableStream
{
    private readonly TcpClient _client;

    private TcpTransport(TcpClient client)
    {
        _client = client;
    }

    public Stream Stream => _client.GetStream();

    public static async Task<TcpTransport> ConnectAsync(string host, int port)
    {
        var client = new TcpClient();
        await client.ConnectAsync(host, port);
        return new TcpTransport(client);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
