using System.Diagnostics;
using BlueType.Agent.Bluetooth;
using BlueType.Agent.Application.Commands;
using BlueType.Agent.Core;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Models;
using BlueType.Agent.Network;

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
    {
        _inputInjector = new InputInjector();
        _clipboardService = new ClipboardService();
        _shortcutProfiles = new ShortcutProfileDispatcher();

        var deviceRegistry = new DeviceRegistry();
        var commandRouter = new CommandRouter(_inputInjector, _clipboardService);
        var authService = new AuthService(deviceRegistry, promptForAuthorizationAsync);
        _activeSessionManager = new ActiveSessionManager();
        var sessionProcessor = new SessionProcessor(commandRouter, authService, _inputInjector, _activeSessionManager, _shortcutProfiles, promptForAuthorizationAsync);

        _tcpServer = new TcpServer(sessionProcessor);
        _bluetoothServer = new BluetoothServer(sessionProcessor);

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
