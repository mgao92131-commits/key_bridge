using System.Text.Json;
using BlueType.Agent.Application.Ports;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Infrastructure.Shortcuts;
using BlueType.Agent.Transport;
using BlueType.Protocol;

namespace BlueType.Agent.Application.Shortcuts;

internal interface IShortcutProfileDispatcher
{
    void RegisterSession(Guid sessionId, ClientSession session);
    void UnregisterSession(Guid sessionId);
}

internal sealed class ShortcutProfileDispatcher : IShortcutProfileDispatcher, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan StableDuration = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly IReadOnlyList<ShortcutProfileDefinition> _profiles;
    private readonly IForegroundAppProvider _foregroundAppProvider;
    private readonly ShortcutProfileMatcher _matcher;
    private readonly Task _pollTask;

    private ActiveShortcutSession? _activeSession;
    private string? _observedProcess;
    private long _observedSince;
    private string? _lastSentProfileKey;

    public ShortcutProfileDispatcher(
        ShortcutProfileMatcher matcher,
        IForegroundAppProvider foregroundAppProvider,
        IShortcutProfileRepository profileRepository)
    {
        _profiles = profileRepository.Load();
        _foregroundAppProvider = foregroundAppProvider;
        _matcher = matcher;
        _pollTask = Task.Run(PollAsync);
    }

    public void RegisterSession(Guid sessionId, ClientSession session)
    {
        lock (_gate)
        {
            _activeSession = new ActiveShortcutSession(sessionId, session);
            _lastSentProfileKey = null;
        }

        _ = SendCurrentAsync(CancellationToken.None);
    }

    public void UnregisterSession(Guid sessionId)
    {
        lock (_gate)
        {
            if (_activeSession?.SessionId == sessionId)
            {
                _activeSession = null;
                _lastSentProfileKey = null;
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _pollTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        _stop.Dispose();
    }

    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                var processName = _foregroundAppProvider.GetCurrentAppId();
                var now = Environment.TickCount64;
                var stable = false;

                lock (_gate)
                {
                    if (!string.Equals(_observedProcess, processName, StringComparison.OrdinalIgnoreCase))
                    {
                        _observedProcess = processName;
                        _observedSince = now;
                    }
                    else if (now - _observedSince >= StableDuration.TotalMilliseconds)
                    {
                        stable = true;
                    }
                }

                if (stable)
                {
                    await SendCurrentAsync(_stop.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.Error("Shortcut profile foreground monitor stopped.", ex);
        }
    }

    private async Task SendCurrentAsync(CancellationToken cancellationToken)
    {
        ActiveShortcutSession? session;
        string? processName;
        lock (_gate)
        {
            session = _activeSession;
            processName = _observedProcess ?? _foregroundAppProvider.GetCurrentAppId();
        }

        if (session is null)
        {
            return;
        }

        var profile = _matcher.Match(_profiles, processName);
        var profileKey = profile?.Id ?? string.Empty;

        lock (_gate)
        {
            if (_activeSession?.SessionId != session.SessionId)
            {
                return;
            }

            if (string.Equals(_lastSentProfileKey, profileKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastSentProfileKey = profileKey;
        }

        var envelope = JsonProtocol.CreateEnvelope(
            Guid.NewGuid().ToString(),
            Responses.ShortcutProfile,
            new ShortcutProfilePayload(profile?.Name, profile?.Profile));

        try
        {
            await session.Session.WriteAsync(envelope, cancellationToken);
            AppLogger.Info(profile is null
                ? $"Sent shortcut profile reset for foreground process {processName ?? "unknown"}."
                : $"Sent shortcut profile '{profile.Id}' for foreground process {processName ?? "unknown"}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("Failed to send shortcut profile.", ex);
        }
    }

    private sealed record ActiveShortcutSession(Guid SessionId, ClientSession Session);
    private sealed record ShortcutProfilePayload(string? Name, JsonElement? Profile);
}
