using BlueType.Agent.Application.Commands;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Models;
using BlueType.Protocol;

namespace BlueType.Agent.Application.Sessions;

internal sealed class SessionCommandLoop
{
    private readonly CommandRouter _commandRouter;
    private readonly SessionHeartbeat _heartbeat;
    private readonly SessionHandshake _handshake;

    public SessionCommandLoop(
        CommandRouter commandRouter,
        SessionHeartbeat heartbeat,
        SessionHandshake handshake)
    {
        _commandRouter = commandRouter;
        _heartbeat = heartbeat;
        _handshake = handshake;
    }

    public async Task RunAsync(
        SessionExecutionContext context,
        SessionLifecycle lifecycle,
        CancellationTokenSource sessionLifetime,
        Action recordInboundActivity)
    {
        var sessionToken = sessionLifetime.Token;
        while (!sessionToken.IsCancellationRequested)
        {
            var envelope = await context.Session.ReadAsync(sessionToken);
            if (envelope is null)
            {
                break;
            }

            recordInboundActivity();

            if (await _heartbeat.TryHandleInboundAsync(context.Session, envelope, sessionToken))
            {
                continue;
            }

            var handshakeResult = await _handshake.TryHandleAsync(
                context.Session,
                envelope,
                lifecycle,
                context.RemoteAddress,
                context.Transport,
                context.OnState,
                context.OnMessage,
                context.SessionId,
                context.DisconnectCurrentSession,
                sessionToken);
            if (handshakeResult == HandshakeResult.Continue)
            {
                continue;
            }

            if (handshakeResult == HandshakeResult.Terminate)
            {
                break;
            }

            Envelope response;
            var lifecycleError = lifecycle.ValidateCommandEnvelope(envelope);
            if (lifecycleError is not null)
            {
                response = lifecycleError;
                await context.Session.WriteAsync(response, sessionToken);
                if (string.Equals(response.Type, Responses.Error, StringComparison.Ordinal) &&
                    response.Payload.TryGetProperty("code", out var code) &&
                    string.Equals(code.GetString(), "SESSION_REPLACED", StringComparison.Ordinal))
                {
                    break;
                }
            }
            else
            {
                try
                {
                    response = await _commandRouter.RouteAsync(envelope, sessionToken);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Failed to route {context.Transport} command {envelope.Type}.", ex);
                    response = _commandRouter.CreateError(envelope.Id, "SERVER_ERROR", ex.Message);
                }
            }

            if (lifecycleError is null)
            {
                await context.Session.WriteAsync(response, sessionToken);
            }
        }
    }
}
