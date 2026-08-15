using BlueType.Agent.Core;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Application.Sessions;
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

    // AcceptBluetoothClient is a sync blocking call; Stop() may not always unblock promptly.
    protected override TimeSpan ListenLoopShutdownTimeout => TimeSpan.FromSeconds(2);

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
        var listener = _listener
            ?? throw new ObjectDisposedException(nameof(BluetoothServer));

        // LongRunning: do not occupy a thread-pool worker with a blocking RFCOMM accept.
        // CancellationToken cannot abort AcceptBluetoothClient once it is inside the native wait;
        // StopListening() must unblock it, and StopAsync bounds the wait.
        return Task.Factory.StartNew(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return listener.AcceptBluetoothClient();
            },
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
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
        var listener = Interlocked.Exchange(ref _listener, null);
        if (listener is null)
        {
            return;
        }

        try
        {
            listener.Stop();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Bluetooth listener Stop() failed.", ex);
        }
    }
}
