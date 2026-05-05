using BlueType.Protocol;

namespace BlueType.Agent.Core;

internal interface ICommandHandler
{
    Task<Envelope?> TryHandleAsync(Envelope envelope, CancellationToken cancellationToken);
}
