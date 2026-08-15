using BlueType.Agent.Models;

namespace BlueType.Agent.Presentation.Tray;

internal sealed class SettingsForm : Form
{
    private readonly Action _disconnectAction;
    private readonly Label _statusValueLabel;
    private readonly TextBox _appDataPathTextBox;
    private readonly Button _disconnectButton;

    public SettingsForm(Action disconnectAction)
    {
        _disconnectAction = disconnectAction;

        Text = "BlueType Agent Settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 160);

        var statusTitleLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 20),
            Text = "Connection status:",
        };

        _statusValueLabel = new Label
        {
            AutoSize = true,
            Location = new Point(140, 20),
            Text = ConnectionState.Idle.ToString(),
        };

        _disconnectButton = new Button
        {
            Location = new Point(360, 14),
            Size = new Size(124, 28),
            Text = "Disconnect",
            Enabled = false,
        };
        _disconnectButton.Click += (_, _) => _disconnectAction.Invoke();

        var appDataTitleLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 58),
            Text = "App data path:",
        };

        _appDataPathTextBox = new TextBox
        {
            Location = new Point(16, 82),
            ReadOnly = true,
            Size = new Size(580, 23),
        };

        var closeButton = new Button
        {
            Location = new Point(516, 16),
            Size = new Size(80, 28),
            Text = "Close",
        };
        closeButton.Click += (_, _) => Hide();

        Controls.Add(statusTitleLabel);
        Controls.Add(_statusValueLabel);
        Controls.Add(_disconnectButton);
        Controls.Add(appDataTitleLabel);
        Controls.Add(_appDataPathTextBox);
        Controls.Add(closeButton);

        ThemeHelper.ApplyDarkTheme(this);
    }

    public void UpdateState(ConnectionState state, string appDataPath)
    {
        _statusValueLabel.Text = state.ToString();
        _appDataPathTextBox.Text = appDataPath;
        _disconnectButton.Enabled = CanDisconnect(state);
    }

    private static bool CanDisconnect(ConnectionState state)
    {
        return state is ConnectionState.ClientConnected
            or ConnectionState.Authenticating
            or ConnectionState.PendingApproval
            or ConnectionState.Connected;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnFormClosing(e);
    }
}
