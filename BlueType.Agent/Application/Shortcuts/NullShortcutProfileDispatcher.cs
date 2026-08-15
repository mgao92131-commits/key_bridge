using BlueType.Protocol;

namespace BlueType.Agent.Application.Shortcuts;

internal sealed class NullShortcutProfileDispatcher : IShortcutProfileDispatcher
{
    public static readonly NullShortcutProfileDispatcher Instance = new();

    private NullShortcutProfileDispatcher()
    {
    }

    public void RegisterSession(Guid sessionId, Func<Envelope, CancellationToken, Task> writeAsync)
    {
    }

    public void UnregisterSession(Guid sessionId)
    {
    }
}
