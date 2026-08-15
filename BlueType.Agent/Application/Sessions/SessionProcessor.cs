using BlueType.Agent.Models;
using BlueType.Agent.Application.Commands;
using BlueType.Agent.Application.Authorization;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Transport;
using BlueType.Protocol;

namespace BlueType.Agent.Application.Sessions;

internal sealed class SessionProcessor
{
    private readonly CommandRouter _commandRouter;
    private readonly SessionHandshake _handshake;
    private readonly SessionHeartbeat _heartbeat;
    private readonly ActiveSessionManager _activeSessionManager;
    private readonly SessionCleanup _cleanup;

    public SessionProcessor(
        CommandRouter commandRouter,
        AuthService authService,
        InputInjector inputInjector)
        : this(
            commandRouter,
            authService,
            inputInjector,
            new ActiveSessionManager(),
            NullShortcutProfileDispatcher.Instance,
            (_, _) => Task.FromResult(AuthPromptDecision.Deny),
            inputInjector,
            new SessionHeartbeat())
    {
    }

    public SessionProcessor(
        CommandRouter commandRouter,
        AuthService authService,
        InputInjector inputInjector,
        ActiveSessionManager activeSessionManager,
        IShortcutProfileDispatcher shortcutProfiles,
        Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> promptAsync)
        : this(
            commandRouter,
            authService,
            inputInjector,
            activeSessionManager,
            shortcutProfiles,
            promptAsync,
            inputInjector,
            new SessionHeartbeat())
    {
    }

    internal SessionProcessor(
        CommandRouter commandRouter,
        AuthService authService,
        InputInjector inputInjector,
        ActiveSessionManager activeSessionManager,
        IShortcutProfileDispatcher shortcutProfiles,
        Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> promptAsync,
        IInputRelease inputRelease,
        SessionHeartbeat heartbeat)
    {
        _commandRouter = commandRouter;
        var helloHandler = new SessionHelloHandler(commandRouter, authService, activeSessionManager, promptAsync);
        _handshake = new SessionHandshake(helloHandler, shortcutProfiles);
        _heartbeat = heartbeat;
        _activeSessionManager = activeSessionManager;
        _cleanup = new SessionCleanup(activeSessionManager, shortcutProfiles, inputRelease);
    }

    public async Task RunAsync(
        ClientSession session,
        string? remoteAddress,
        string transport,
        Action<ConnectionState>? onState,
        Action<string>? onMessage,
        CancellationToken cancellationToken)
    {
        await RunAsync(session, remoteAddress, transport, onState, onMessage, Guid.NewGuid(), () => { }, cancellationToken);
    }

    public async Task RejectBusyClientAsync(ClientSession session, CancellationToken cancellationToken)
    {
        await session.WriteAsync(_commandRouter.CreateError("busy", "BUSY", "Another device is already connected."), cancellationToken);
    }

    public async Task RunAsync(
        ClientSession session,
        string? remoteAddress,
        string transport,
        Action<ConnectionState>? onState,
        Action<string>? onMessage,
        Guid sessionId,
        Action disconnectCurrentSession,
        CancellationToken cancellationToken)
    {
        var lifecycle = new SessionLifecycle(_commandRouter, _activeSessionManager, sessionId);
        long lastInboundAt = Environment.TickCount64;
        using var sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sessionToken = sessionLifetime.Token;
        var heartbeatTask = _heartbeat.RunAsync(
            session,
            transport,
            remoteAddress,
            () => Volatile.Read(ref lastInboundAt),
            onMessage,
            sessionLifetime);

        onState?.Invoke(ConnectionState.ClientConnected);
        AppLogger.Info($"{transport} client connected from {remoteAddress ?? "unknown"}.");

        try
        {
            while (!sessionToken.IsCancellationRequested)
            {
                var envelope = await session.ReadAsync(sessionToken);
                if (envelope is null)
                {
                    break;
                }

                Volatile.Write(ref lastInboundAt, Environment.TickCount64);

                if (await _heartbeat.TryHandleInboundAsync(session, envelope, sessionToken))
                {
                    continue;
                }

                var handshakeResult = await _handshake.TryHandleAsync(
                    session,
                    envelope,
                    lifecycle,
                    remoteAddress,
                    transport,
                    onState,
                    onMessage,
                    sessionId,
                    disconnectCurrentSession,
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
                    await session.WriteAsync(response, sessionToken);
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
                        AppLogger.Error($"Failed to route {transport} command {envelope.Type}.", ex);
                        response = _commandRouter.CreateError(envelope.Id, "SERVER_ERROR", ex.Message);
                    }
                }

                if (lifecycleError is null)
                {
                    await session.WriteAsync(response, sessionToken);
                }
            }
        }
        catch (OperationCanceledException) when (sessionLifetime.IsCancellationRequested)
        {
        }
        finally
        {
            await _cleanup.ExecuteAsync(sessionId, sessionLifetime, heartbeatTask);
        }
    }
}
