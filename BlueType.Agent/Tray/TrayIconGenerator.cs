using System.Drawing;
using System.Runtime.InteropServices;
using BlueType.Agent.Core;
using BlueType.Agent.Models;

namespace BlueType.Agent.Tray;

internal enum AlertLevel
{
    None,
    Info,
    Warning,
    Error
}

internal sealed class TrayIconGenerator : IDisposable
{
    private readonly Icon _baseIcon;
    private Icon? _currentIcon;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public TrayIconGenerator()
    {
        // Extract the main application icon
        _baseIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)!;
    }

    public Icon CreateIcon(ConnectionState connectionState, AlertLevel alertLevel)
    {
        // Use 32x32 for better tray resolution if possible, otherwise use base icon size
        int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.DrawIcon(_baseIcon, new Rectangle(0, 0, size, size));

            Color? dotColor = null;
            if (alertLevel == AlertLevel.Error)
            {
                dotColor = Color.FromArgb(255, 82, 82); // Modern Red
            }
            else if (alertLevel == AlertLevel.Warning)
            {
                dotColor = Color.FromArgb(255, 183, 77); // Modern Orange
            }
            else if (connectionState is ConnectionState.Connected or ConnectionState.ClientConnected)
            {
                dotColor = ThemeColors.Success; // Green for success
            }
            else if (connectionState is ConnectionState.Authenticating or ConnectionState.PendingApproval)
            {
                dotColor = ThemeColors.Primary; // Purple for "in-between" or authenticating state
            }

            if (dotColor.HasValue)
            {
                float dotSize = size * 0.35f;
                float x = size - dotSize - 1;
                float y = size - dotSize - 1;

                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // Draw subtle shadow/border for better visibility on different taskbar backgrounds
                using (var brush = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                {
                    g.FillEllipse(brush, x - 0.5f, y - 0.5f, dotSize + 1.5f, dotSize + 1.5f);
                }

                using (var brush = new SolidBrush(dotColor.Value))
                {
                    g.FillEllipse(brush, x, y, dotSize, dotSize);
                }
            }
        }

        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            // Icon.FromHandle doesn't take ownership of the handle. 
            // We must clone it to get a managed Icon that owns its own handle, 
            // then we can safely destroy the HICON from GetHicon.
            var newIcon = (Icon)Icon.FromHandle(hIcon).Clone();
            
            _currentIcon?.Dispose();
            _currentIcon = newIcon;
            return _currentIcon;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    public void Dispose()
    {
        _baseIcon.Dispose();
        _currentIcon?.Dispose();
    }
}