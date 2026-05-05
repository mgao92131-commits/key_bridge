using System.Net;
using System.Net.Sockets;
using System.Threading;
using BlueType.Agent.Core;
using BlueType.Agent.Transport;

namespace BlueType.Agent.Network;

internal sealed class TcpServer : ConnectionServerBase<TcpClient>
{
    private readonly TcpListener _listener;

    public TcpServer(SessionProcessor sessionProcessor)
        : base(sessionProcessor)
    {
        _listener = new TcpListener(IPAddress.Any, PortConstants.DefaultTcpPort);
    }

    protected override string TransportDisplayName => "TCP";

    protected override string SessionTransportName => "wifi";

    public void Start()
    {
        try
        {
            _listener.Start();
            StartAcceptLoop(
                $"TCP server is listening on port {PortConstants.DefaultTcpPort}. If LAN clients cannot connect, allow the app through Windows Firewall.",
                $"TCP server listening on port {PortConstants.DefaultTcpPort}.");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            EmitServerMessage($"TCP port {PortConstants.DefaultTcpPort} is already in use.");
            AppLogger.Error($"TCP port {PortConstants.DefaultTcpPort} is already in use.", ex);
        }
        catch (Exception ex)
        {
            EmitServerMessage($"Failed to start TCP server: {ex.Message}");
            AppLogger.Error("Failed to start TCP server.", ex);
        }
    }

    protected override Task<TcpClient> AcceptClientAsync(CancellationToken cancellationToken)
    {
        return _listener.AcceptTcpClientAsync(cancellationToken).AsTask();
    }

    protected override ClientSession CreateSession(TcpClient client)
    {
        return new ClientSession(client.GetStream());
    }

    protected override void CloseClient(TcpClient? client)
    {
        client?.Close();
    }

    protected override void DisposeClient(TcpClient client)
    {
        client.Dispose();
    }

    protected override string? GetRemoteAddress(TcpClient client)
    {
        return client.Client.RemoteEndPoint?.ToString();
    }

    protected override void StopListening()
    {
        _listener.Stop();
    }
}
