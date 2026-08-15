using BlueType.Agent.Application.Commands;
using BlueType.Agent.Application.Ports;
using BlueType.Agent.Application.Shortcuts;
using BlueType.Agent.Infrastructure.Logging;

namespace BlueType.Agent.Application.Sessions;

internal sealed class SessionCleanup
{
    private readonly ActiveSessionManager _activeSessionManager;
    private readonly IShortcutProfileCoordinator _shortcutProfiles;
    private readonly IInputRelease _inputRelease;

    public SessionCleanup(
        ActiveSessionManager activeSessionManager,
        IShortcutProfileCoordinator shortcutProfiles,
        IInputRelease inputRelease)
    {
        _activeSessionManager = activeSessionManager;
        _shortcutProfiles = shortcutProfiles;
        _inputRelease = inputRelease;
    }

    public async Task ExecuteAsync(
        Guid sessionId,
        CancellationTokenSource sessionLifetime,
        Task heartbeatTask)
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
