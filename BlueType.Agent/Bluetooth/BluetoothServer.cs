using BlueType.Agent.Core;
using BlueType.Agent.Transport;
using InTheHand.Net.Sockets;

namespace BlueType.Agent.Bluetooth;

internal sealed class BluetoothServer : ConnectionServerBase<BluetoothClient>
{
    private BluetoothListener? _listener;

    public BluetoothServer(SessionProcessor sessionProcessor)
        : base(sessionProcessor)
    {
    }
    
    protected override string TransportDisplayName => "Bluetooth";

    protected override string SessionTransportName => "bluetooth";

    public void Start()
    {
        try
        {
            _listener = new BluetoothListener(ServiceConstants.ServiceUuid)
            {
                ServiceName = ServiceConstants.ServiceName,
            };
            _listener.Start();
            StartAcceptLoop(
                "Bluetooth RFCOMM server is listening.",
                "Bluetooth RFCOMM server listening.");
        }
        catch (Exception ex)
        {
            EmitServerMessage($"Failed to start Bluetooth server: {ex.Message}");
            AppLogger.Error("Failed to start Bluetooth server.", ex);
        }
    }

    protected override Task<BluetoothClient> AcceptClientAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => _listener!.AcceptBluetoothClient(), cancellationToken);
    }

    protected override ClientSession CreateSession(BluetoothClient client)
    {
        return new ClientSession(client.GetStream());
    }

    protected override void CloseClient(BluetoothClient? client)
    {
        client?.Close();
    }

    protected override void DisposeClient(BluetoothClient client)
    {
        client.Dispose();
    }

    protected override string? GetRemoteAddress(BluetoothClient client)
    {
        return TryGetRemoteAddress(client);
    }

    private static string? TryGetRemoteAddress(BluetoothClient client)
    {
        try
        {
            return client.Client.RemoteEndPoint?.ToString() ?? client.RemoteMachineName;
        }
        catch
        {
            try
            {
                return client.RemoteMachineName;
            }
            catch
            {
                return null;
            }
        }
    }

    protected override void StopListening()
    {
        _listener?.Stop();
    }
}
