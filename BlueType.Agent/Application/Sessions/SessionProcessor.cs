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
    private readonly SessionHelloHandler _helloHandler;
    private readonly SessionHeartbeat _heartbeat;
    private readonly IInputRelease _inputRelease;
    private readonly ActiveSessionManager _activeSessionManager;
    private readonly IShortcutProfileDispatcher _shortcutProfiles;

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
        _helloHandler = new SessionHelloHandler(commandRouter, authService, activeSessionManager, promptAsync);
        _heartbeat = heartbeat;
        _inputRelease = inputRelease;
        _activeSessionManager = activeSessionManager;
        _shortcutProfiles = shortcutProfiles;
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

                if (string.Equals(envelope.Type, BlueType.Protocol.Commands.Hello, StringComparison.Ordinal))
                {
                    if (lifecycle.IsAuthorized)
                    {
                        await session.WriteAsync(lifecycle.CreateDuplicateHelloError(envelope.Id), sessionToken);
                        continue;
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
                        sessionToken);

                    if (!authorized)
                    {
                        break;
                    }

                    lifecycle.MarkAuthorized();
                    _shortcutProfiles.RegisterSession(sessionId, session);
                    continue;
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
            sessionLifetime.Cancel();
            _shortcutProfiles.UnregisterSession(sessionId);
            _activeSessionManager.Deactivate(sessionId);
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }

            try
            {
                await _inputRelease.ReleaseAllKeysAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to release keyboard keys after session shutdown.", ex);
            }

            try
            {
                await _inputRelease.ReleaseAllMouseButtonsAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to release mouse buttons after session shutdown.", ex);
            }
        }
    }
}
