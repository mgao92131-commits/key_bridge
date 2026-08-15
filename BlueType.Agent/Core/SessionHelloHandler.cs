using System.Text.Json;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Models;
using BlueType.Agent.Transport;
using BlueType.Protocol;

namespace BlueType.Agent.Core;

internal sealed class SessionHelloHandler
{
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromSeconds(60);

    private readonly CommandRouter _commandRouter;
    private readonly AuthService _authService;
    private readonly ActiveSessionManager _activeSessionManager;
    private readonly Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> _promptAsync;

    public SessionHelloHandler(CommandRouter commandRouter, AuthService authService)
        : this(
            commandRouter,
            authService,
            new ActiveSessionManager(),
            (_, _) => Task.FromResult(AuthPromptDecision.Deny))
    {
    }

    public SessionHelloHandler(
        CommandRouter commandRouter,
        AuthService authService,
        ActiveSessionManager activeSessionManager,
        Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> promptAsync)
    {
        _commandRouter = commandRouter;
        _authService = authService;
        _activeSessionManager = activeSessionManager;
        _promptAsync = promptAsync;
    }

    public Task<bool> HandleAsync(
        ClientSession session,
        Envelope envelope,
        string? remoteAddress,
        string transport,
        Action<ConnectionState>? onState,
        Action<string>? onMessage,
        CancellationToken cancellationToken)
    {
        return HandleAsync(session, envelope, remoteAddress, transport, onState, onMessage, Guid.NewGuid(), () => { }, cancellationToken);
    }

    public async Task<bool> HandleAsync(
        ClientSession session,
        Envelope envelope,
        string? remoteAddress,
        string transport,
        Action<ConnectionState>? onState,
        Action<string>? onMessage,
        Guid sessionId,
        Action disconnectCurrentSession,
        CancellationToken cancellationToken)
    {
        HelloInfo helloInfo;
        try
        {
            helloInfo = ParseHello(envelope.Payload);
        }
        catch (Exception ex)
        {
            var invalidHello = _commandRouter.CreateError(envelope.Id, "INVALID_PAYLOAD", ex.Message);
            await session.WriteAsync(invalidHello, cancellationToken);
            return false;
        }

        onState?.Invoke(ConnectionState.Authenticating);

        AuthResult authResult;
        var knownDeviceResult = _authService.TryAuthorizeKnownDevice(helloInfo, envelope.Token, remoteAddress, transport);
        if (knownDeviceResult.IsAuthorized)
        {
            authResult = knownDeviceResult;
        }
        else
        {
            var pending = JsonProtocol.CreateEnvelope(
                envelope.Id,
                Responses.AuthPending,
                new { timeoutSec = (int)ApprovalTimeout.TotalSeconds, message = "Please confirm on Windows" });
            await session.WriteAsync(pending, cancellationToken);
            onState?.Invoke(ConnectionState.PendingApproval);

            using var approvalTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            approvalTimeout.CancelAfter(ApprovalTimeout);

            try
            {
                authResult = await _authService.RequestApprovalAsync(
                    helloInfo,
                    remoteAddress,
                    transport,
                    approvalTimeout.Token);
            }
            catch (OperationCanceledException) when (approvalTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                authResult = AuthResult.Error("AUTH_TIMEOUT", "Authorization timed out.");
            }
        }

        if (!authResult.IsAuthorized)
        {
            var error = _commandRouter.CreateError(
                envelope.Id,
                authResult.ErrorCode ?? "NOT_AUTHORIZED",
                authResult.ErrorMessage ?? "Authorization failed.");
            await session.WriteAsync(error, cancellationToken);
            AppLogger.Info(
                $"Authorization failed for {transport} device {helloInfo.DeviceName} ({helloInfo.DeviceId}): {authResult.ErrorCode}.");
            return false;
        }

        var activationResult = await _activeSessionManager.ActivateAsync(
            new ActiveSessionCandidate(
                SessionId: sessionId,
                DeviceId: helloInfo.DeviceId,
                DeviceName: helloInfo.DeviceName,
                Transport: transport,
                RemoteAddress: remoteAddress,
                Disconnect: disconnectCurrentSession),
            ConfirmTakeoverAsync,
            cancellationToken);
        if (!activationResult.IsActivated)
        {
            var active = activationResult.ReplacedSession;
            var error = _commandRouter.CreateError(
                envelope.Id,
                "BUSY",
                active is null
                    ? "Another device is already connected."
                    : $"Another device is already controlling this PC: {active.DeviceName}.");
            await session.WriteAsync(error, cancellationToken);
            AppLogger.Info(
                $"Rejected takeover for {transport} device {helloInfo.DeviceName} ({helloInfo.DeviceId}) because active session remained with {active?.DeviceName ?? "unknown"}.");
            return false;
        }

        activationResult.DisconnectReplacedSession?.Invoke();
        if (activationResult.ReplacedSession is { } replacedSession)
        {
            var takeoverMessage = string.Equals(replacedSession.DeviceId, helloInfo.DeviceId, StringComparison.OrdinalIgnoreCase)
                ? $"Restored active {transport} session for {helloInfo.DeviceName}."
                : $"Switched active session from {replacedSession.DeviceName} to {helloInfo.DeviceName}.";
            onMessage?.Invoke(takeoverMessage);
            AppLogger.Info(takeoverMessage);
        }

        await session.WriteAsync(CreateAuthResultEnvelope(envelope.Id, authResult), cancellationToken);
        onState?.Invoke(ConnectionState.Connected);
        var authorizationMessage = knownDeviceResult.IsAuthorized
            ? $"Authorized known {transport} device: {helloInfo.DeviceName}"
            : $"Authorized {transport} device: {helloInfo.DeviceName}";
        onMessage?.Invoke(authorizationMessage);
        AppLogger.Info($"{authorizationMessage} ({helloInfo.DeviceId}).");
        return true;
    }

    private static HelloInfo ParseHello(JsonElement payload)
    {
        if (!payload.TryGetProperty("deviceId", out var deviceIdProp) || deviceIdProp.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Missing hello.deviceId.");
        }

        if (!payload.TryGetProperty("deviceName", out var deviceNameProp) || deviceNameProp.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Missing hello.deviceName.");
        }

        string? appVersion = null;
        if (payload.TryGetProperty("appVersion", out var appVersionProp) && appVersionProp.ValueKind == JsonValueKind.String)
        {
            appVersion = appVersionProp.GetString();
        }

        return new HelloInfo(
            DeviceId: deviceIdProp.GetString() ?? string.Empty,
            DeviceName: deviceNameProp.GetString() ?? string.Empty,
            AppVersion: appVersion);
    }

    private static Envelope CreateAuthResultEnvelope(string requestId, AuthResult authResult)
    {
        return JsonProtocol.CreateEnvelope(
            requestId,
            Responses.AuthResult,
            new
            {
                ok = true,
                token = authResult.Token,
                persistToken = authResult.PersistToken,
                trusted = authResult.PersistToken,
            });
    }

    private async Task<bool> ConfirmTakeoverAsync(SessionTakeoverRequest request, CancellationToken cancellationToken)
    {
        var decision = await _promptAsync(
            new AuthPromptRequest(
                Mode: AuthPromptMode.SwitchActiveDevice,
                DeviceId: request.IncomingDeviceId,
                DeviceName: request.IncomingDeviceName,
                RemoteAddress: request.IncomingRemoteAddress,
                Transport: request.IncomingTransport,
                ActiveDeviceName: request.ActiveSession.DeviceName,
                ActiveRemoteAddress: request.ActiveSession.RemoteAddress,
                ActiveTransport: request.ActiveSession.Transport),
            cancellationToken);
        return decision == AuthPromptDecision.AllowOnce;
    }
}
