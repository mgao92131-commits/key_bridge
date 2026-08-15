using System.Diagnostics;
using BlueType.Agent.Native;

namespace BlueType.Agent.Infrastructure.Shortcuts;

internal static class ForegroundProcessReader
{
    public static string? CurrentProcessName()
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
