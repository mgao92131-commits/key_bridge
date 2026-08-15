using System.Diagnostics;
using BlueType.Agent.Application.Sessions;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Models;
using BlueType.Agent.Transport;

namespace BlueType.Agent.Host;

internal sealed class AgentRuntime
{
    private readonly IDisposable _inputInjector;
    private readonly IDisposable _clipboardService;
    private readonly IDisposable _shortcutProfiles;
    private readonly ActiveSessionManager _activeSessionManager;
    private readonly IRuntimeTransport _tcpServer;
    private readonly IRuntimeTransport _bluetoothServer;
    private readonly object _stopLock = new();
    private Task? _stopTask;
    private int _stopped;

    internal AgentRuntime(
        IDisposable inputInjector,
        IDisposable clipboardService,
        IDisposable shortcutProfiles,
        ActiveSessionManager activeSessionManager,
        IRuntimeTransport tcpServer,
        IRuntimeTransport bluetoothServer)
    {
        _inputInjector = inputInjector;
        _clipboardService = clipboardService;
        _shortcutProfiles = shortcutProfiles;
        _activeSessionManager = activeSessionManager;
        _tcpServer = tcpServer;
        _bluetoothServer = bluetoothServer;

        _tcpServer.ConnectionStateChanged += ForwardConnectionStateChanged;
        _tcpServer.ServerMessage += ForwardServerMessage;
        _bluetoothServer.ConnectionStateChanged += ForwardConnectionStateChanged;
        _bluetoothServer.ServerMessage += ForwardServerMessage;
    }

    public event Action<ConnectionState>? ConnectionStateChanged;

    public event Action<string>? ServerMessage;

    public void Start()
    {
        _tcpServer.Start();
        _bluetoothServer.Start();
    }

    public bool DisconnectActiveClient()
    {
        // Prefer the protocol-controlling session (authorized device driving input).
        if (_activeSessionManager.TryDisconnectActive())
        {
            ConnectionStateChanged?.Invoke(ConnectionState.Disconnecting);
            ServerMessage?.Invoke("Disconnecting active client...");
            return true;
        }

        // Fallback for pre-auth connections (Connected/Authenticating/PendingApproval UI)
        // when no controlling session has been activated yet.
        var disconnected = _tcpServer.DisconnectLatestClient() | _bluetoothServer.DisconnectLatestClient();
        if (!disconnected)
        {
            return false;
        }

        ConnectionStateChanged?.Invoke(ConnectionState.Disconnecting);
        ServerMessage?.Invoke("Disconnecting active client...");
        return true;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_stopLock)
        {
            if (_stopTask is not null)
            {
                return _stopTask;
            }

            _stopTask = StopCoreAsync(cancellationToken);
            return _stopTask;
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        var startedAt = Stopwatch.StartNew();
        AppLogger.Info("Stopping agent runtime.");

        try
        {
            await Task.WhenAll(
                _bluetoothServer.StopAsync(cancellationToken),
                _tcpServer.StopAsync(cancellationToken));
        }
        finally
        {
            _bluetoothServer.ConnectionStateChanged -= ForwardConnectionStateChanged;
            _bluetoothServer.ServerMessage -= ForwardServerMessage;
            _tcpServer.ConnectionStateChanged -= ForwardConnectionStateChanged;
            _tcpServer.ServerMessage -= ForwardServerMessage;

            DisposeResource(_shortcutProfiles, "shortcut profiles");
            DisposeResource(_clipboardService, "clipboard service");
            DisposeResource(_inputInjector, "input injector");
            DisposeResource(_bluetoothServer, "Bluetooth server");
            DisposeResource(_tcpServer, "TCP server");

            AppLogger.Info($"Agent runtime stopped in {startedAt.ElapsedMilliseconds} ms.");
        }
    }

    private void ForwardConnectionStateChanged(ConnectionState state)
    {
        ConnectionStateChanged?.Invoke(state);
    }

    private void ForwardServerMessage(string message)
    {
        ServerMessage?.Invoke(message);
    }

    private static void DisposeResource(IDisposable resource, string displayName)
    {
        try
        {
            resource.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to dispose {displayName} during agent runtime shutdown.", ex);
        }
    }
}
