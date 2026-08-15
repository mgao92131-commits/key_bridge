using BlueType.Agent.Application.Commands;
using BlueType.Agent.Application.Shortcuts;
using BlueType.Agent.Models;
using BlueType.Agent.Transport;
using BlueType.Protocol;

namespace BlueType.Agent.Application.Sessions;

internal enum HandshakeResult
{
    NotHandled,
    Continue,
    Terminate,
}

internal sealed class SessionHandshake
{
    private readonly SessionHelloHandler _helloHandler;
    private readonly IShortcutProfileDispatcher _shortcutProfiles;

    public SessionHandshake(
        SessionHelloHandler helloHandler,
        IShortcutProfileDispatcher shortcutProfiles)
    {
        _helloHandler = helloHandler;
        _shortcutProfiles = shortcutProfiles;
    }

    public async Task<HandshakeResult> TryHandleAsync(
        ClientSession session,
        Envelope envelope,
        SessionLifecycle lifecycle,
        string? remoteAddress,
        string transport,
        Action<ConnectionState>? onState,
        Action<string>? onMessage,
        Guid sessionId,
        Action disconnectCurrentSession,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope.Type, BlueType.Protocol.Commands.Hello, StringComparison.Ordinal))
        {
            return HandshakeResult.NotHandled;
        }

        if (lifecycle.IsAuthorized)
        {
            await session.WriteAsync(lifecycle.CreateDuplicateHelloError(envelope.Id), cancellationToken);
            return HandshakeResult.Continue;
        }

        var authorized = await _helloHandler.HandleAsync(
            session,
            envelope,
            remoteAddress,
            transport,
            onState,
            onMessage,
            sessionId,
            disconnectCurrentSession,
            cancellationToken);

        if (!authorized)
        {
            return HandshakeResult.Terminate;
        }

        lifecycle.MarkAuthorized();
        _shortcutProfiles.RegisterSession(sessionId, session);
        return HandshakeResult.Continue;
    }
}
