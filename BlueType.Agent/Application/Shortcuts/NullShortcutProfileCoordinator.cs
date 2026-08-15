using BlueType.Protocol;

namespace BlueType.Agent.Application.Shortcuts;

internal sealed class NullShortcutProfileCoordinator : IShortcutProfileCoordinator
{
    public static readonly NullShortcutProfileCoordinator Instance = new();

    private NullShortcutProfileCoordinator()
    {
    }

    public void RegisterSession(Guid sessionId, Func<Envelope, CancellationToken, Task> writeAsync)
    {
    }

    public void UnregisterSession(Guid sessionId)
    {
    }
}
