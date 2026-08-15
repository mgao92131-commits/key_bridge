using System.Runtime.InteropServices;
using BlueType.Agent.Native;

namespace BlueType.Agent.Infrastructure.Input;

internal sealed class WindowsMouseInjector
{
    private readonly HashSet<string> _pressedMouseButtons = new(StringComparer.OrdinalIgnoreCase);

    public void Move(int dx, int dy)
    {
        var inputs = new[]
        {
            CreateMouseMoveInput(dx, dy),
        };

        SendInputs(inputs);
    }

    public void Click(string button, int repeat)
    {
        var definition = ResolveMouseButton(button);
        if (_pressedMouseButtons.Contains(definition.Name))
        {
            throw new InvalidOperationException($"Mouse button is already pressed: {button}");
        }

        var inputs = new List<Win32.INPUT>(repeat * 2);
        for (var index = 0; index < repeat; index++)
        {
            inputs.Add(CreateMouseButtonInput(definition.DownFlag));
            inputs.Add(CreateMouseButtonInput(definition.UpFlag));
        }

        SendInputs(inputs);
    }

    public void SetButtonState(string button, bool isDown)
    {
        var definition = ResolveMouseButton(button);
        var isAlreadyDown = _pressedMouseButtons.Contains(definition.Name);
        if (isDown == isAlreadyDown)
        {
            return;
        }

        SendInputs([CreateMouseButtonInput(isDown ? definition.DownFlag : definition.UpFlag)]);
        if (isDown)
        {
            _pressedMouseButtons.Add(definition.Name);
        }
        else
        {
            _pressedMouseButtons.Remove(definition.Name);
        }
    }

    public void ReleaseAll()
    {
        if (_pressedMouseButtons.Count == 0)
        {
            return;
        }

        var inputs = new List<Win32.INPUT>(_pressedMouseButtons.Count);
        foreach (var definition in _pressedMouseButtons
            .Select(ResolveMouseButton)
            .OrderBy(definition => definition.Name, StringComparer.Ordinal))
        {
            inputs.Add(CreateMouseButtonInput(definition.UpFlag));
        }

        SendInputs(inputs);
        _pressedMouseButtons.Clear();
    }

    public void Scroll(int deltaX, int deltaY)
    {
        var inputs = new List<Win32.INPUT>(2);
        if (deltaY != 0)
        {
            inputs.Add(CreateMouseWheelInput(deltaY * Win32.MouseWheelDelta, horizontal: false));
        }

        if (deltaX != 0)
        {
            inputs.Add(CreateMouseWheelInput(deltaX * Win32.MouseWheelDelta, horizontal: true));
        }

        SendInputs(inputs);
    }

    private static Win32.INPUT CreateMouseMoveInput(int dx, int dy)
    {
        return new Win32.INPUT
        {
            type = Win32.InputMouse,
            U = new Win32.InputUnion
            {
                mi = new Win32.MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = 0,
                    dwFlags = Win32.MouseEventFMove,
                },
            },
        };
    }

    private static Win32.INPUT CreateMouseButtonInput(uint flags)
    {
        return new Win32.INPUT
        {
            type = Win32.InputMouse,
            U = new Win32.InputUnion
            {
                mi = new Win32.MOUSEINPUT
                {
                    dwFlags = flags,
                },
            },
        };
    }

    private static Win32.INPUT CreateMouseWheelInput(int delta, bool horizontal)
    {
        return new Win32.INPUT
        {
            type = Win32.InputMouse,
            U = new Win32.InputUnion
            {
                mi = new Win32.MOUSEINPUT
                {
                    mouseData = unchecked((uint)delta),
                    dwFlags = horizontal ? Win32.MouseEventFHWheel : Win32.MouseEventFWheel,
                },
            },
        };
    }

    private static MouseButtonDefinition ResolveMouseButton(string button)
    {
        return button.Trim().ToUpperInvariant() switch
        {
            "LEFT" => new MouseButtonDefinition("LEFT", Win32.MouseEventFLeftDown, Win32.MouseEventFLeftUp),
            "RIGHT" => new MouseButtonDefinition("RIGHT", Win32.MouseEventFRightDown, Win32.MouseEventFRightUp),
            "MIDDLE" => new MouseButtonDefinition("MIDDLE", Win32.MouseEventFMiddleDown, Win32.MouseEventFMiddleUp),
            _ => throw new InvalidOperationException($"Unsupported mouse button: {button}"),
        };
    }

    private static void SendInputs(IReadOnlyList<Win32.INPUT> inputs)
    {
        var sent = Win32.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Win32.INPUT>());
        if (sent != inputs.Count)
        {
            throw new InvalidOperationException(
                $"SendInput failed or was blocked. sent={sent} expected={inputs.Count} win32={Marshal.GetLastWin32Error()}");
        }
    }

    private readonly record struct MouseButtonDefinition(string Name, uint DownFlag, uint UpFlag);
}
