using System.Runtime.InteropServices;
using BlueType.Agent.Native;

namespace BlueType.Agent.Infrastructure.Input;

internal interface IWindowsInputSender
{
    void Send(IReadOnlyList<Win32.INPUT> inputs);
}

internal sealed class WindowsInputSender : IWindowsInputSender
{
    public void Send(IReadOnlyList<Win32.INPUT> inputs)
    {
        var sent = Win32.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Win32.INPUT>());
        if (sent != inputs.Count)
        {
            throw new InvalidOperationException(
                $"SendInput failed or was blocked. sent={sent} expected={inputs.Count} win32={Marshal.GetLastWin32Error()}");
        }
    }
}
