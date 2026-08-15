using BlueType.Agent.Application.Commands;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Models;
using BlueType.Protocol;

namespace BlueType.Agent.Application.Sessions;

internal sealed class SessionCommandLoop
{
    private readonly CommandDispatcher _commandDispatcher;
    private readonly SessionHeartbeat _heartbeat;
    private readonly SessionHandshake _handshake;

    public SessionCommandLoop(
        CommandDispatcher commandDispatcher,
        SessionHeartbeat heartbeat,
        SessionHandshake handshake)
    {
        _commandDispatcher = commandDispatcher;
        _heartbeat = heartbeat;
        _handshake = handshake;
    }

    public async Task RunAsync(
        SessionExecutionContext context,
        SessionLifecycle lifecycle,
        CancellationToken cancellationToken,
        Action recordInboundActivity)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var envelope = await context.Session.ReadAsync(cancellationToken);
            if (envelope is null)
            {
                break;
            }

            recordInboundActivity();

            if (await _heartbeat.TryHandleInboundAsync(context.Session, envelope, cancellationToken))
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
                cancellationToken);
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
                await context.Session.WriteAsync(response, cancellationToken);
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
                    response = await _commandDispatcher.DispatchAsync(envelope, cancellationToken);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Failed to route {context.Transport} command {envelope.Type}.", ex);
                    response = _commandDispatcher.CreateError(envelope.Id, "SERVER_ERROR", ex.Message);
                }
            }

            if (lifecycleError is null)
            {
                await context.Session.WriteAsync(response, cancellationToken);
            }
        }
    }
}
