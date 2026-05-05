using InTheHand.Net;
using InTheHand.Net.Sockets;

namespace BlueType.TestClient.Transports;

internal sealed class BluetoothTransport : IAsyncDisposableStream
{
    private static readonly Guid ServiceUuid = new("5F8C2C1D-9A25-4A20-9F0B-30D8D0F7E913");

    private readonly BluetoothClient _client;

    private BluetoothTransport(BluetoothClient client)
    {
        _client = client;
    }

    public Stream Stream => _client.GetStream();

    public static async Task<BluetoothTransport> ConnectAsync(string address)
    {
        var client = new BluetoothClient();
        var btAddress = BluetoothAddress.Parse(address);
        await Task.Run(() => client.Connect(btAddress, ServiceUuid));
        return new BluetoothTransport(client);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
