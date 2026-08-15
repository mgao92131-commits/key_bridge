using System.Text.Json;
using BlueType.Agent.Application.Ports;
using BlueType.Agent.Application.Shortcuts;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Protocol;

namespace BlueType.Agent.Infrastructure.Shortcuts;

internal sealed class ShortcutProfileSessionPublisher : IShortcutProfileSessionPublisher
{
    private readonly object _gate = new();
    private ActiveShortcutSession? _activeSession;

    public void RegisterSession(Guid sessionId, Func<Envelope, CancellationToken, Task> writeAsync)
    {
        lock (_gate)
        {
            _activeSession = new ActiveShortcutSession(sessionId, writeAsync);
        }
    }

    public void UnregisterSession(Guid sessionId)
    {
        lock (_gate)
        {
            if (_activeSession?.SessionId == sessionId)
            {
                _activeSession = null;
            }
        }
    }

    public async Task PublishAsync(
        Guid sessionId,
        ShortcutProfileDefinition? profile,
        string? processName,
        CancellationToken cancellationToken)
    {
        Func<Envelope, CancellationToken, Task>? writeAsync;
        lock (_gate)
        {
            writeAsync = _activeSession?.SessionId == sessionId
                ? _activeSession.WriteAsync
                : null;
        }

        if (writeAsync is null)
        {
            return;
        }

        var envelope = JsonProtocol.CreateEnvelope(
            Guid.NewGuid().ToString(),
            Responses.ShortcutProfile,
            new ShortcutProfilePayload(profile?.Name, profile?.Profile));

        try
        {
            await writeAsync(envelope, cancellationToken);
            AppLogger.Info(profile is null
                ? $"Sent shortcut profile reset for foreground process {processName ?? "unknown"}."
                : $"Sent shortcut profile '{profile.Id}' for foreground process {processName ?? "unknown"}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("Failed to send shortcut profile.", ex);
        }
    }

    private sealed record ActiveShortcutSession(
        Guid SessionId,
        Func<Envelope, CancellationToken, Task> WriteAsync);

    private sealed record ShortcutProfilePayload(string? Name, JsonElement? Profile);
}
