using System.Diagnostics;
using BlueType.Agent.Application.Ports;
using BlueType.Agent.Native;

namespace BlueType.Agent.Infrastructure.Shortcuts;

internal sealed class WindowsForegroundAppProvider : IForegroundAppProvider
{
    public string? GetCurrentAppId()
    {
        var window = Win32.GetForegroundWindow();
        if (window == 0)
        {
            return null;
        }

        _ = Win32.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
