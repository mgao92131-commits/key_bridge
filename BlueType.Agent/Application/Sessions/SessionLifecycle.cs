using BlueType.Protocol;
using BlueType.Agent.Application.Commands;

namespace BlueType.Agent.Application.Sessions;

internal sealed class SessionLifecycle
{
    private readonly CommandDispatcher _commandDispatcher;
    private readonly ActiveSessionManager _activeSessionManager;
    private readonly Guid _sessionId;
    private bool _isAuthorized;

    public SessionLifecycle(
        CommandDispatcher commandDispatcher,
        ActiveSessionManager activeSessionManager,
        Guid sessionId)
    {
        _commandDispatcher = commandDispatcher;
        _activeSessionManager = activeSessionManager;
        _sessionId = sessionId;
    }

    public bool IsAuthorized => _isAuthorized;

    public void MarkAuthorized()
    {
        _isAuthorized = true;
    }

    public Envelope? ValidateCommandEnvelope(Envelope envelope)
    {
        if (!_isAuthorized)
        {
            return _commandDispatcher.CreateError(
                envelope.Id,
                "NOT_AUTHORIZED",
                "Send HELLO and complete authorization first.");
        }

        if (!_activeSessionManager.IsActive(_sessionId))
        {
            return _commandDispatcher.CreateError(
                envelope.Id,
                "SESSION_REPLACED",
                "This connection is no longer the active control session.");
        }

        return null;
    }

    public Envelope CreateDuplicateHelloError(string requestId)
    {
        return _commandDispatcher.CreateError(requestId, "INVALID_PAYLOAD", "HELLO already completed.");
    }
}
