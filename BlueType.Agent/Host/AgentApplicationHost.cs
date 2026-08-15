using System.Diagnostics;
using BlueType.Agent.Bootstrap;
using BlueType.Agent.Application.Commands;
using BlueType.Agent.Transport.Bluetooth;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Models;
using BlueType.Agent.Application.Sessions;
using BlueType.Agent.Transport.Tcp;

namespace BlueType.Agent.Host;

internal sealed class AgentApplicationHost : IDisposable
{
    private readonly InputInjector _inputInjector;
    private readonly ClipboardService _clipboardService;
    private readonly ShortcutProfileDispatcher _shortcutProfiles;
    private readonly ActiveSessionManager _activeSessionManager;
    private readonly TcpServer _tcpServer;
    private readonly BluetoothServer _bluetoothServer;
    private readonly object _stopLock = new();
    private Task? _stopTask;
    private int _stopped;

    public AgentApplicationHost(Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> promptForAuthorizationAsync)
        : this(AgentCompositionRoot.Create(promptForAuthorizationAsync))
    {
    }

    internal AgentApplicationHost(AgentCompositionRoot.Components components)
    {
        _inputInjector = components.InputInjector;
        _clipboardService = components.ClipboardService;
        _shortcutProfiles = components.ShortcutProfiles;
        _activeSessionManager = components.ActiveSessionManager;
        _tcpServer = components.TcpServer;
        _bluetoothServer = components.BluetoothServer;

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

    public void Dispose()
    {
        try
        {
            if (!StopAsync().Wait(TimeSpan.FromSeconds(5)))
            {
                AppLogger.Warn("Agent host Dispose timed out waiting for StopAsync.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Agent host Dispose stop failed.", ex);
        }
        finally
        {
            _bluetoothServer.Dispose();
            _tcpServer.Dispose();
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        var startedAt = Stopwatch.StartNew();
        AppLogger.Info("Stopping agent host.");

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

            _shortcutProfiles.Dispose();
            _clipboardService.Dispose();
            _inputInjector.Dispose();

            AppLogger.Info($"Agent host stopped in {startedAt.ElapsedMilliseconds} ms.");
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
}
