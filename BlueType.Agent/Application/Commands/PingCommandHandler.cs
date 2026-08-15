using BlueType.Agent.Infrastructure.Logging;
using BlueType.Protocol;
using ProtocolCommands = BlueType.Protocol.Commands;
using ProtocolResponses = BlueType.Protocol.Responses;

namespace BlueType.Agent.Application.Commands;

internal sealed class PingCommandHandler : ICommandHandler
{
    private static readonly string[] CommandTypes = [ProtocolCommands.Ping];

    public IReadOnlyCollection<string> SupportedCommands => CommandTypes;

    public Task<Envelope> HandleAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        AppLogger.Info("Handled command: ping.");
        return Task.FromResult(
            JsonProtocol.CreateEnvelope(envelope.Id, ProtocolResponses.Pong, new { ok = true }));
    }
}
