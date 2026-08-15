using BlueType.Agent.Host;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Infrastructure.Persistence;
using BlueType.Agent.Models;

namespace BlueType.Agent.Presentation.Tray;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly SynchronizationContext _syncContext;
    private readonly AuthorizationPromptPresenter _authorizationPrompt;
    private readonly AgentApplicationHost _applicationHost;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _disconnectMenuItem;
    private readonly ToolStripMenuItem _settingsMenuItem;
    private readonly ToolStripMenuItem _exitMenuItem;
    private readonly SettingsForm _settingsForm;

    private ConnectionState _connectionState = ConnectionState.Listening;
    private readonly TrayIconGenerator _iconGenerator;
    private readonly System.Windows.Forms.Timer _alertResetTimer;
    private AlertLevel _alertLevel = AlertLevel.None;
    private string _lastServerMessage = string.Empty;
    private int _isShuttingDown;

    public TrayAppContext()
    {
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _authorizationPrompt = new AuthorizationPromptPresenter(_syncContext);
        _applicationHost = new AgentApplicationHost(_authorizationPrompt.ShowAsync);
        _settingsForm = new SettingsForm(DisconnectActiveClient);
        _iconGenerator = new TrayIconGenerator();

        _alertResetTimer = new System.Windows.Forms.Timer
        {
            Interval = 5000 // 5 seconds to reset alert level
        };
        _alertResetTimer.Tick += (s, e) =>
        {
            _alertLevel = AlertLevel.None;
            _alertResetTimer.Stop();
            UpdateStatus(_connectionState);
        };

        _statusMenuItem = new ToolStripMenuItem();
        _disconnectMenuItem = new ToolStripMenuItem("Disconnect Current Client", null, OnDisconnectActiveClient)
        {
            Enabled = false,
        };
        _settingsMenuItem = new ToolStripMenuItem("Settings", null, OnOpenSettings);
        _exitMenuItem = new ToolStripMenuItem("Exit", null, OnExit);

        _notifyIcon = new NotifyIcon
        {
            Icon = _iconGenerator.CreateIcon(ConnectionState.Listening, AlertLevel.None),
            Text = "BlueType Agent",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };

        _notifyIcon.ContextMenuStrip.Items.AddRange(
        [
            _statusMenuItem,
            _disconnectMenuItem,
            new ToolStripSeparator(),
            _settingsMenuItem,
            _exitMenuItem,
        ]);

        ThemeHelper.ApplyToContextMenu(_notifyIcon.ContextMenuStrip);

        _notifyIcon.DoubleClick += OnOpenSettings;
        _applicationHost.ConnectionStateChanged += HandleConnectionStateChanged;
        _applicationHost.ServerMessage += HandleServerMessage;
        UpdateStatus(ConnectionState.Listening);
        _applicationHost.Start();
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _isShuttingDown) != 0)
        {
            return;
        }

        if (_authorizationPrompt.HasActivePrompt)
        {
            _authorizationPrompt.BringToFrontIfActive();
            return;
        }

        if (_settingsForm.Visible)
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm.UpdateState(_connectionState, AppSettingsStore.BaseDirectory);
        _settingsForm.Show();
        _settingsForm.BringToFront();
    }

    private async void OnExit(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) != 0)
        {
            return;
        }

        _exitMenuItem.Enabled = false;
        _settingsMenuItem.Enabled = false;
        _disconnectMenuItem.Enabled = false;

        AppLogger.Info("Agent shutdown requested.");

        try
        {
            AppLogger.Info("Closing authorization prompt.");
            _authorizationPrompt.CloseActivePrompt();

            await _applicationHost.StopAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Agent shutdown failed.", ex);
        }

        ExitThread();
    }

    private void OnDisconnectActiveClient(object? sender, EventArgs e)
    {
        DisconnectActiveClient();
    }

    public void UpdateStatus(ConnectionState state)
    {
        _connectionState = state;
        _statusMenuItem.Enabled = false;
        _statusMenuItem.Text = $"Status: {state}";
        _disconnectMenuItem.Enabled = CanDisconnect(state);

        _notifyIcon.Icon = _iconGenerator.CreateIcon(state, _alertLevel);

        string tooltip = $"BlueType Agent - {state}";
        if (!string.IsNullOrEmpty(_lastServerMessage) && _alertLevel != AlertLevel.None)
        {
            tooltip += $"\n{_lastServerMessage}";
        }
        _notifyIcon.Text = TruncateTooltip(tooltip);

        _settingsForm.UpdateState(_connectionState, AppSettingsStore.BaseDirectory);
    }

    private static string TruncateTooltip(string text)
    {
        // Windows NotifyIcon text limit is usually 63 or 127 characters depending on OS version
        if (text.Length > 127) return text.Substring(0, 124) + "...";
        return text;
    }

    private void HandleConnectionStateChanged(ConnectionState state)
    {
        _syncContext.Post(_ => UpdateStatus(state), null);
    }

    private void HandleServerMessage(string message)
    {
        AppLogger.Info(message);
        _lastServerMessage = message;

        // Map common messages to alert levels if needed
        _alertLevel = AlertLevel.Info;
        if (message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            _alertLevel = AlertLevel.Error;
        }
        else if (message.Contains("warn", StringComparison.OrdinalIgnoreCase))
        {
            _alertLevel = AlertLevel.Warning;
        }

        _syncContext.Post(
            _ =>
            {
                UpdateStatus(_connectionState);
                _alertResetTimer.Stop();
                _alertResetTimer.Start();
            },
            null);
    }

    private void DisconnectActiveClient()
    {
        _applicationHost.DisconnectActiveClient();
    }

    private static bool CanDisconnect(ConnectionState state)
    {
        return state is ConnectionState.ClientConnected
            or ConnectionState.Authenticating
            or ConnectionState.PendingApproval
            or ConnectionState.Connected;
    }

    protected override void ExitThreadCore()
    {
        AppLogger.Info("Disposing tray resources.");

        _alertResetTimer.Stop();
        _alertResetTimer.Dispose();
        _settingsForm.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;
        _notifyIcon.Dispose();
        _iconGenerator.Dispose();
        _settingsForm.Dispose();

        // Host should already be stopped in OnExit; Dispose is an idempotent safety net.
        _applicationHost.Dispose();

        AppLogger.Info("Tray resources disposed.");
        base.ExitThreadCore();
    }
}
