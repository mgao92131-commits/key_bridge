using BlueType.Agent.Transport;

namespace BlueType.Agent.Application.Shortcuts;

internal sealed class NullShortcutProfileDispatcher : IShortcutProfileDispatcher
{
    public static readonly NullShortcutProfileDispatcher Instance = new();

    private NullShortcutProfileDispatcher()
    {
    }

    public void RegisterSession(Guid sessionId, ClientSession session)
    {
    }

    public void UnregisterSession(Guid sessionId)
    {
    }
}
