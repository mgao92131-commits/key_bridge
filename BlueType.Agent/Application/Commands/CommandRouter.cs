using BlueType.Protocol;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Infrastructure.Logging;

namespace BlueType.Agent.Application.Commands;

internal sealed class CommandRouter
{
    private readonly IReadOnlyList<ICommandHandler> _handlers;

    public CommandRouter(InputInjector inputInjector, ClipboardService clipboardService)
    {
        _handlers =
        [
            new KeyboardCommandHandler(inputInjector),
            new MouseCommandHandler(inputInjector),
            new ClipboardCommandHandler(clipboardService),
        ];
    }

    public async Task<Envelope> RouteAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        switch (envelope.Type)
        {
            case BlueType.Protocol.Commands.Ping:
                AppLogger.Info("Handled command: ping.");
                return JsonProtocol.CreateEnvelope(envelope.Id, BlueType.Protocol.Commands.Pong, new { ok = true });
        }

        foreach (var handler in _handlers)
        {
            if (handler.SupportedCommands.Contains(envelope.Type, StringComparer.Ordinal))
            {
                return await handler.HandleAsync(envelope, cancellationToken);
            }
        }

        return CreateError(envelope.Id, "INVALID_PAYLOAD", $"Unsupported message type: {envelope.Type}");
    }

    public Envelope CreateError(string id, string code, string message)
    {
        return JsonProtocol.CreateEnvelope(id, Responses.Error, new { code, message });
    }
}
