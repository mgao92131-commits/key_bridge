using BlueType.Agent.Models;
using BlueType.Agent.Application.Commands;
using BlueType.Agent.Application.Authorization;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Transport;
using BlueType.Protocol;

namespace BlueType.Agent.Application.Sessions;

internal sealed class SessionCoordinator
{
    private readonly CommandDispatcher _commandDispatcher;
    private readonly SessionHeartbeat _heartbeat;
    private readonly SessionCommandLoop _commandLoop;
    private readonly ActiveSessionManager _activeSessionManager;
    private readonly SessionCleanup _cleanup;

    public SessionCoordinator(
        CommandDispatcher commandDispatcher,
        AuthService authService,
        InputInjector inputInjector)
        : this(
            commandDispatcher,
            authService,
            inputInjector,
            new ActiveSessionManager(),
            NullShortcutProfileDispatcher.Instance,
            (_, _) => Task.FromResult(AuthPromptDecision.Deny),
            inputInjector,
            new SessionHeartbeat())
    {
    }

    public SessionCoordinator(
        CommandDispatcher commandDispatcher,
        AuthService authService,
        InputInjector inputInjector,
        ActiveSessionManager activeSessionManager,
        IShortcutProfileDispatcher shortcutProfiles,
        Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> promptAsync)
        : this(
            commandDispatcher,
            authService,
            inputInjector,
            activeSessionManager,
            shortcutProfiles,
            promptAsync,
            inputInjector,
            new SessionHeartbeat())
    {
    }

    internal SessionCoordinator(
        CommandDispatcher commandDispatcher,
        AuthService authService,
        InputInjector inputInjector,
        ActiveSessionManager activeSessionManager,
        IShortcutProfileDispatcher shortcutProfiles,
        Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> promptAsync,
        IInputRelease inputRelease,
        SessionHeartbeat heartbeat)
    {
        _commandDispatcher = commandDispatcher;
        var helloHandler = new SessionHelloHandler(commandDispatcher, authService, activeSessionManager, promptAsync);
        var handshake = new SessionHandshake(helloHandler, shortcutProfiles);
        _heartbeat = heartbeat;
        _commandLoop = new SessionCommandLoop(commandDispatcher, heartbeat, handshake);
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
        await RunAsync(
            new SessionExecutionContext(
                session,
                Guid.NewGuid(),
                remoteAddress,
                transport,
                onState,
                onMessage,
                () => { }),
            cancellationToken);
    }

    public async Task RejectBusyClientAsync(ClientSession session, CancellationToken cancellationToken)
    {
        await session.WriteAsync(_commandDispatcher.CreateError("busy", "BUSY", "Another device is already connected."), cancellationToken);
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
        await RunAsync(
            new SessionExecutionContext(
                session,
                sessionId,
                remoteAddress,
                transport,
                onState,
                onMessage,
                disconnectCurrentSession),
            cancellationToken);
    }

    internal async Task RunAsync(
        SessionExecutionContext context,
        CancellationToken cancellationToken)
    {
        var lifecycle = new SessionLifecycle(_commandDispatcher, _activeSessionManager, context.SessionId);
        long lastInboundAt = Environment.TickCount64;
        using var sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sessionToken = sessionLifetime.Token;
        var heartbeatTask = _heartbeat.RunAsync(
            context.Session,
            context.Transport,
            context.RemoteAddress,
            () => Volatile.Read(ref lastInboundAt),
            context.OnMessage,
            sessionLifetime);

        context.OnState?.Invoke(ConnectionState.ClientConnected);
        AppLogger.Info($"{context.Transport} client connected from {context.RemoteAddress ?? "unknown"}.");

        try
        {
            await _commandLoop.RunAsync(
                context,
                lifecycle,
                sessionToken,
                () => Volatile.Write(ref lastInboundAt, Environment.TickCount64));
        }
        catch (OperationCanceledException) when (sessionLifetime.IsCancellationRequested)
        {
        }
        finally
        {
            await _cleanup.ExecuteAsync(context.SessionId, sessionLifetime, heartbeatTask);
        }
    }
}
