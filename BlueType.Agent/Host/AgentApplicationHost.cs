using BlueType.Agent.Bluetooth;
using BlueType.Agent.Core;
using BlueType.Agent.Models;
using BlueType.Agent.Network;
using BlueType.Agent.Native;

namespace BlueType.Agent.Host;

internal sealed class AgentApplicationHost : IDisposable
{
    private readonly InputInjector _inputInjector;
    private readonly ClipboardService _clipboardService;
    private readonly ShortcutProfileDispatcher _shortcutProfiles;
    private readonly TcpServer _tcpServer;
    private readonly BluetoothServer _bluetoothServer;

    public AgentApplicationHost(Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> promptForAuthorizationAsync)
    {
        _inputInjector = new InputInjector();
        _clipboardService = new ClipboardService();
        _shortcutProfiles = new ShortcutProfileDispatcher();

        var deviceRegistry = new DeviceRegistry();
        var commandRouter = new CommandRouter(_inputInjector, _clipboardService);
        var authService = new AuthService(deviceRegistry, promptForAuthorizationAsync);
        var activeSessionManager = new ActiveSessionManager();
        var sessionProcessor = new SessionProcessor(commandRouter, authService, _inputInjector, activeSessionManager, _shortcutProfiles, promptForAuthorizationAsync);

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
        var disconnected = _tcpServer.DisconnectActiveClient() | _bluetoothServer.DisconnectActiveClient();
        if (!disconnected)
        {
            return false;
        }

        ConnectionStateChanged?.Invoke(ConnectionState.Disconnecting);
        ServerMessage?.Invoke("Disconnecting active client...");
        return true;
    }

    public void Dispose()
    {
        _bluetoothServer.ConnectionStateChanged -= ForwardConnectionStateChanged;
        _bluetoothServer.ServerMessage -= ForwardServerMessage;
        _tcpServer.ConnectionStateChanged -= ForwardConnectionStateChanged;
        _tcpServer.ServerMessage -= ForwardServerMessage;

        _bluetoothServer.Dispose();
        _tcpServer.Dispose();
        _shortcutProfiles.Dispose();
        _clipboardService.Dispose();
        _inputInjector.Dispose();
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
