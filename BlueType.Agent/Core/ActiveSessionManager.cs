using System.Threading;
using BlueType.Agent.Models;

namespace BlueType.Agent.Core;

internal sealed class ActiveSessionManager
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveSessionRegistration? _activeSession;

    public bool IsActive(Guid sessionId)
    {
        _gate.Wait();
        try
        {
            return _activeSession?.SessionId == sessionId;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Disconnects the protocol-controlling session (the device currently driving input).
    /// Returns false when no authorized controlling session exists.
    /// </summary>
    public bool TryDisconnectActive()
    {
        Action? disconnect;

        _gate.Wait();
        try
        {
            if (_activeSession is null)
            {
                return false;
            }

            disconnect = _activeSession.Disconnect;
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            disconnect();
        }
        catch
        {
            // Best effort only.
        }

        return true;
    }

    public async Task<SessionActivationResult> ActivateAsync(
        ActiveSessionCandidate candidate,
        Func<SessionTakeoverRequest, CancellationToken, Task<bool>> confirmTakeoverAsync,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            ActiveSessionSnapshot? snapshotToConfirm;

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_activeSession is null || _activeSession.SessionId == candidate.SessionId)
                {
                    _activeSession = candidate.ToRegistration();
                    return SessionActivationResult.Activated();
                }

                if (string.Equals(_activeSession.DeviceId, candidate.DeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    var previous = _activeSession;
                    _activeSession = candidate.ToRegistration();
                    return SessionActivationResult.Takeover(previous.ToSnapshot(), previous.Disconnect);
                }

                snapshotToConfirm = _activeSession.ToSnapshot();
            }
            finally
            {
                _gate.Release();
            }

            var approved = await confirmTakeoverAsync(
                new SessionTakeoverRequest(
                    IncomingDeviceId: candidate.DeviceId,
                    IncomingDeviceName: candidate.DeviceName,
                    IncomingRemoteAddress: candidate.RemoteAddress,
                    IncomingTransport: candidate.Transport,
                    ActiveSession: snapshotToConfirm),
                cancellationToken);
            if (!approved)
            {
                return SessionActivationResult.Denied(snapshotToConfirm);
            }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_activeSession is null || _activeSession.SessionId == candidate.SessionId)
                {
                    _activeSession = candidate.ToRegistration();
                    return SessionActivationResult.Activated();
                }

                if (_activeSession.SessionId != snapshotToConfirm.SessionId)
                {
                    continue;
                }

                var previous = _activeSession;
                _activeSession = candidate.ToRegistration();
                return SessionActivationResult.Takeover(previous.ToSnapshot(), previous.Disconnect);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public void Deactivate(Guid sessionId)
    {
        _gate.Wait();
        try
        {
            if (_activeSession?.SessionId == sessionId)
            {
                _activeSession = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

internal sealed record ActiveSessionCandidate(
    Guid SessionId,
    string DeviceId,
    string DeviceName,
    string Transport,
    string? RemoteAddress,
    Action Disconnect)
{
    public ActiveSessionRegistration ToRegistration()
    {
        return new ActiveSessionRegistration(
            SessionId,
            DeviceId,
            DeviceName,
            Transport,
            RemoteAddress,
            Disconnect);
    }
}

internal sealed record ActiveSessionSnapshot(
    Guid SessionId,
    string DeviceId,
    string DeviceName,
    string Transport,
    string? RemoteAddress);

internal sealed record SessionTakeoverRequest(
    string IncomingDeviceId,
    string IncomingDeviceName,
    string? IncomingRemoteAddress,
    string IncomingTransport,
    ActiveSessionSnapshot ActiveSession);

internal sealed class SessionActivationResult
{
    private SessionActivationResult(bool activated, ActiveSessionSnapshot? replacedSession, Action? disconnectReplacedSession)
    {
        IsActivated = activated;
        ReplacedSession = replacedSession;
        DisconnectReplacedSession = disconnectReplacedSession;
    }

    public bool IsActivated { get; }

    public ActiveSessionSnapshot? ReplacedSession { get; }

    public Action? DisconnectReplacedSession { get; }

    public static SessionActivationResult Activated()
    {
        return new SessionActivationResult(activated: true, replacedSession: null, disconnectReplacedSession: null);
    }

    public static SessionActivationResult Takeover(ActiveSessionSnapshot replacedSession, Action disconnectReplacedSession)
    {
        return new SessionActivationResult(activated: true, replacedSession, disconnectReplacedSession);
    }

    public static SessionActivationResult Denied(ActiveSessionSnapshot activeSession)
    {
        return new SessionActivationResult(activated: false, activeSession, disconnectReplacedSession: null);
    }
}

internal sealed record ActiveSessionRegistration(
    Guid SessionId,
    string DeviceId,
    string DeviceName,
    string Transport,
    string? RemoteAddress,
    Action Disconnect)
{
    public ActiveSessionSnapshot ToSnapshot()
    {
        return new ActiveSessionSnapshot(SessionId, DeviceId, DeviceName, Transport, RemoteAddress);
    }
}
