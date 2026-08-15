using BlueType.Agent.Native;

namespace BlueType.Agent.Infrastructure.Input;

internal sealed class WindowsMouseInjector
{
    private readonly IWindowsInputSender _sender;
    private readonly HashSet<string> _pressedMouseButtons = new(StringComparer.OrdinalIgnoreCase);

    public WindowsMouseInjector(IWindowsInputSender sender)
    {
        _sender = sender;
    }

    public void Move(int dx, int dy)
    {
        var inputs = new[]
        {
            CreateMouseMoveInput(dx, dy),
        };

        _sender.Send(inputs);
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

        _sender.Send(inputs);
    }

    public void SetButtonState(string button, bool isDown)
    {
        var definition = ResolveMouseButton(button);
        var isAlreadyDown = _pressedMouseButtons.Contains(definition.Name);
        if (isDown == isAlreadyDown)
        {
            return;
        }

        _sender.Send([CreateMouseButtonInput(isDown ? definition.DownFlag : definition.UpFlag)]);
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

        _sender.Send(inputs);
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

        _sender.Send(inputs);
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

    private readonly record struct MouseButtonDefinition(string Name, uint DownFlag, uint UpFlag);
}
