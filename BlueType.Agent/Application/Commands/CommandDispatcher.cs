using BlueType.Protocol;
using BlueType.Agent.Infrastructure.Logging;

namespace BlueType.Agent.Application.Commands;

internal sealed class CommandDispatcher
{
    private readonly IReadOnlyDictionary<string, ICommandHandler> _handlers;

    public CommandDispatcher(IEnumerable<ICommandHandler> handlers)
    {
        var index = new Dictionary<string, ICommandHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            foreach (var commandType in handler.SupportedCommands)
            {
                if (!index.TryAdd(commandType, handler))
                {
                    throw new InvalidOperationException(
                        $"Command '{commandType}' is registered by multiple handlers.");
                }
            }
        }

        _handlers = index;
    }

    public Task<Envelope> DispatchAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        switch (envelope.Type)
        {
            case BlueType.Protocol.Commands.Ping:
                AppLogger.Info("Handled command: ping.");
                return Task.FromResult(
                    JsonProtocol.CreateEnvelope(envelope.Id, BlueType.Protocol.Commands.Pong, new { ok = true }));
        }

        if (_handlers.TryGetValue(envelope.Type, out var handler))
        {
            return handler.HandleAsync(envelope, cancellationToken);
        }

        return Task.FromResult(CreateError(envelope.Id, "INVALID_PAYLOAD", $"Unsupported message type: {envelope.Type}"));
    }

    public Envelope CreateError(string id, string code, string message)
    {
        return JsonProtocol.CreateEnvelope(id, Responses.Error, new { code, message });
    }
}
