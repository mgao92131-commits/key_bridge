using BlueType.Protocol;

namespace BlueType.Agent.Application.Commands;

internal interface ICommandHandler
{
    IReadOnlyCollection<string> SupportedCommands { get; }

    Task<Envelope> HandleAsync(Envelope envelope, CancellationToken cancellationToken);
}
