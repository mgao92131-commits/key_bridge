using BlueType.Agent.Models;

namespace BlueType.Agent.Presentation.Tray;

internal sealed class AuthPromptForm : Form
{
    public AuthPromptForm(AuthPromptRequest request)
    {
        var isSwitchPrompt = request.Mode == AuthPromptMode.SwitchActiveDevice;

        Text = isSwitchPrompt ? "Switch Active Device" : "Approve Device";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(460, isSwitchPrompt ? 290 : 240);

        var introLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(428, 48),
            Text = isSwitchPrompt
                ? "A different device wants to take over the current control session on this PC."
                : "A device is requesting permission to control keyboard input on this PC.",
        };

        var deviceNameLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 76),
            Text = $"Device name: {request.DeviceName}",
        };

        var deviceIdLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 104),
            Text = $"Device ID: {request.DeviceId}",
        };

        var transportLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 132),
            Text = $"Transport: {request.Transport}",
        };

        var remoteAddressLabel = new Label
        {
            AutoSize = true,
            Location = new Point(16, 160),
            Text = $"Remote address: {request.RemoteAddress ?? "Unknown"}",
        };

        Control? activeDeviceLabel = null;
        Control? activeTransportLabel = null;

        if (isSwitchPrompt)
        {
            activeDeviceLabel = new Label
            {
                AutoSize = true,
                Location = new Point(16, 196),
                Text = $"Currently active: {request.ActiveDeviceName ?? "Unknown"}",
            };

            activeTransportLabel = new Label
            {
                AutoSize = true,
                Location = new Point(16, 224),
                Text = $"Current endpoint: {request.ActiveTransport ?? "Unknown"} / {request.ActiveRemoteAddress ?? "Unknown"}",
            };
        }

        var rejectButton = new Button
        {
            Location = new Point(16, isSwitchPrompt ? 248 : 192),
            Size = new Size(96, 30),
            Text = isSwitchPrompt ? "Keep Current" : "Reject",
        };
        rejectButton.Click += (_, _) => CloseWith(AuthPromptDecision.Deny);

        var allowOnceButton = new Button
        {
            Location = new Point(isSwitchPrompt ? 348 : 240, isSwitchPrompt ? 248 : 192),
            Size = new Size(96, 30),
            Text = isSwitchPrompt ? "Switch" : "Allow Once",
        };
        allowOnceButton.Click += (_, _) => CloseWith(AuthPromptDecision.AllowOnce);

        var alwaysAllowButton = new Button
        {
            Location = new Point(348, 192),
            Size = new Size(96, 30),
            Text = "Always Allow",
            Visible = !isSwitchPrompt,
        };
        alwaysAllowButton.Click += (_, _) => CloseWith(AuthPromptDecision.AlwaysAllow);

        Controls.Add(introLabel);
        Controls.Add(deviceNameLabel);
        Controls.Add(deviceIdLabel);
        Controls.Add(transportLabel);
        Controls.Add(remoteAddressLabel);
        if (activeDeviceLabel != null)
        {
            Controls.Add(activeDeviceLabel);
        }
        if (activeTransportLabel != null)
        {
            Controls.Add(activeTransportLabel);
        }
        Controls.Add(rejectButton);
        Controls.Add(allowOnceButton);
        Controls.Add(alwaysAllowButton);

        ThemeHelper.ApplyDarkTheme(this);
    }

    public AuthPromptDecision Decision { get; private set; } = AuthPromptDecision.Deny;

    private void CloseWith(AuthPromptDecision decision)
    {
        Decision = decision;
        DialogResult = DialogResult.OK;
        Close();
    }
}
