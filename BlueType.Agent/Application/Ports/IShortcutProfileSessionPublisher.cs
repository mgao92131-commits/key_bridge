using BlueType.Agent.Application.Shortcuts;
using BlueType.Protocol;

namespace BlueType.Agent.Application.Ports;

internal interface IShortcutProfileSessionPublisher
{
    void RegisterSession(Guid sessionId, Func<Envelope, CancellationToken, Task> writeAsync);

    void UnregisterSession(Guid sessionId);

    Task PublishAsync(
        Guid sessionId,
        ShortcutProfileDefinition? profile,
        string? processName,
        CancellationToken cancellationToken);
}
