using BlueType.Agent.Application.Authorization;
using BlueType.Agent.Application.Commands;
using BlueType.Agent.Application.Ports;
using BlueType.Agent.Application.Sessions;
using BlueType.Agent.Application.Shortcuts;
using BlueType.Agent.Domain.Devices;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Infrastructure.Shortcuts;
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
        var inputInjector = new WindowsInputService();
        var clipboardService = new ClipboardService();
        var shortcutProfiles = new ShortcutProfileCoordinator(
            new ShortcutProfileMatcher(),
            new WindowsForegroundAppProvider(),
            new JsonShortcutProfileRepository(),
            new ShortcutProfileSessionPublisher());

        var deviceRegistry = new DeviceRegistry();
        var commandDispatcher = CreateCommandDispatcher(inputInjector, clipboardService);
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

    private static CommandDispatcher CreateCommandDispatcher(
        IInputService inputService,
        IClipboardService clipboardService)
    {
        return new CommandDispatcher(
        [
            new PingCommandHandler(),
            new KeyboardCommandHandler(inputService),
            new MouseCommandHandler(inputService),
            new ClipboardCommandHandler(clipboardService),
        ]);
    }
}
