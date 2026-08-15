using BlueType.Agent.Application.Authorization;
using BlueType.Agent.Application.Commands;
using BlueType.Agent.Application.Sessions;
using BlueType.Agent.Domain.Devices;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Models;
using BlueType.Agent.Transport.Bluetooth;
using BlueType.Agent.Transport.Tcp;

namespace BlueType.Agent.Bootstrap;

internal static class AgentCompositionRoot
{
    internal static Components Create(
        Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> promptForAuthorizationAsync)
    {
        var inputInjector = new InputInjector();
        var clipboardService = new ClipboardService();
        var shortcutProfiles = new ShortcutProfileDispatcher();

        var deviceRegistry = new DeviceRegistry();
        var commandRouter = new CommandRouter(inputInjector, clipboardService);
        var authService = new AuthService(deviceRegistry, promptForAuthorizationAsync);
        var activeSessionManager = new ActiveSessionManager();
        var sessionProcessor = new SessionProcessor(
            commandRouter,
            authService,
            inputInjector,
            activeSessionManager,
            shortcutProfiles,
            promptForAuthorizationAsync);

        var tcpServer = new TcpServer(sessionProcessor);
        var bluetoothServer = new BluetoothServer(sessionProcessor);

        return new Components(
            inputInjector,
            clipboardService,
            shortcutProfiles,
            activeSessionManager,
            tcpServer,
            bluetoothServer);
    }

    internal sealed record Components(
        InputInjector InputInjector,
        ClipboardService ClipboardService,
        ShortcutProfileDispatcher ShortcutProfiles,
        ActiveSessionManager ActiveSessionManager,
        TcpServer TcpServer,
        BluetoothServer BluetoothServer);
}
