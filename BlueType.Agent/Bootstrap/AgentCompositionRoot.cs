using BlueType.Agent.Application.Authorization;
using BlueType.Agent.Application.Commands;
using BlueType.Agent.Application.Sessions;
using BlueType.Agent.Domain.Devices;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Models;
using BlueType.Agent.Host;
using BlueType.Agent.Transport.Bluetooth;
using BlueType.Agent.Transport.Tcp;

namespace BlueType.Agent.Bootstrap;

internal static class AgentCompositionRoot
{
    internal static AgentRuntime Create(
        Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> promptForAuthorizationAsync)
    {
        var inputInjector = new InputInjector();
        var clipboardService = new ClipboardService();
        var shortcutProfiles = new ShortcutProfileDispatcher();

        var deviceRegistry = new DeviceRegistry();
        var commandHandlers = new ICommandHandler[]
        {
            new PingCommandHandler(),
            new KeyboardCommandHandler(inputInjector),
            new MouseCommandHandler(inputInjector),
            new ClipboardCommandHandler(clipboardService),
        };
        var commandDispatcher = new CommandDispatcher(commandHandlers);
        var authService = new AuthService(deviceRegistry, promptForAuthorizationAsync);
        var activeSessionManager = new ActiveSessionManager();
        var sessionCoordinator = new SessionCoordinator(
            commandDispatcher,
            authService,
            inputInjector,
            activeSessionManager,
            shortcutProfiles,
            promptForAuthorizationAsync);

        var tcpServer = new TcpServer(sessionCoordinator);
        var bluetoothServer = new BluetoothServer(sessionCoordinator);

        return new AgentRuntime(
            inputInjector,
            clipboardService,
            shortcutProfiles,
            activeSessionManager,
            tcpServer,
            bluetoothServer);
    }
}
