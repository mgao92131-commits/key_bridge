using BlueType.Protocol;

namespace BlueType.Agent.Application.Commands;

internal interface ICommandHandler
{
    Task<Envelope?> TryHandleAsync(Envelope envelope, CancellationToken cancellationToken);
}
