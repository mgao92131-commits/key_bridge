using BlueType.Agent.Application.Commands;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;

namespace BlueType.Agent.Tests.TestHelpers;

internal static class CommandDispatcherTestFactory
{
    public static CommandDispatcher Create(
        WindowsInputService inputInjector,
        ClipboardService clipboardService)
    {
        return new CommandDispatcher(
        [
            new PingCommandHandler(),
            new KeyboardCommandHandler(inputInjector),
            new MouseCommandHandler(inputInjector),
            new ClipboardCommandHandler(clipboardService),
        ]);
    }
}
