using BlueType.Protocol;

namespace BlueType.Agent.Core;

internal sealed class SessionLifecycle
{
    private readonly CommandRouter _commandRouter;
    private readonly ActiveSessionManager _activeSessionManager;
    private readonly Guid _sessionId;
    private bool _isAuthorized;

    public SessionLifecycle(
        CommandRouter commandRouter,
        ActiveSessionManager activeSessionManager,
        Guid sessionId)
    {
        _commandRouter = commandRouter;
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
            return _commandRouter.CreateError(
                envelope.Id,
                "NOT_AUTHORIZED",
                "Send HELLO and complete authorization first.");
        }

        if (!_activeSessionManager.IsActive(_sessionId))
        {
            return _commandRouter.CreateError(
                envelope.Id,
                "SESSION_REPLACED",
                "This connection is no longer the active control session.");
        }

        return null;
    }

    public Envelope CreateDuplicateHelloError(string requestId)
    {
        return _commandRouter.CreateError(requestId, "INVALID_PAYLOAD", "HELLO already completed.");
    }
}
