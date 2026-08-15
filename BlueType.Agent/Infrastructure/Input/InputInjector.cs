using System.Runtime.InteropServices;
using BlueType.Agent.Application.Ports;
using BlueType.Agent.Native;

namespace BlueType.Agent.Infrastructure.Input;

internal interface IInputRelease
{
    Task ReleaseAllKeysAsync(CancellationToken cancellationToken = default);

    Task ReleaseAllMouseButtonsAsync(CancellationToken cancellationToken = default);
}

internal sealed class InputInjector : IDisposable, IInputService, IInputRelease
{
    private readonly InputExecutionQueue _executionQueue;
    private readonly HashSet<string> _pressedMouseButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly WindowsKeyboardInjector _keyboard;

    public InputInjector()
    {
        _executionQueue = new InputExecutionQueue();
        _keyboard = new WindowsKeyboardInjector();
    }

    public Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() => _keyboard.SendText(text), cancellationToken);
    }

    public Task TapKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => _keyboard.TapKey(key), cancellationToken);
    }

    public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => _keyboard.PressKey(key), cancellationToken);
    }

    public Task ReleaseKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => _keyboard.ReleaseKey(key), cancellationToken);
    }

    public Task ReleaseAllKeysAsync(CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(_keyboard.ReleaseAll, cancellationToken);
    }

    public Task SendComboAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() => _keyboard.SendCombo(keys), cancellationToken);
    }

    public Task MoveMouseAsync(int dx, int dy, CancellationToken cancellationToken = default)
    {
        if (dx == 0 && dy == 0)
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() => MoveMouseCore(dx, dy), cancellationToken);
    }

    public Task ClickMouseAsync(string button, int repeat, CancellationToken cancellationToken = default)
    {
        if (repeat <= 0)
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() => ClickMouseCore(button, repeat), cancellationToken);
    }

    public Task PressMouseAsync(string button, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => SetMouseButtonStateCore(button, isDown: true), cancellationToken);
    }

    public Task ReleaseMouseAsync(string button, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => SetMouseButtonStateCore(button, isDown: false), cancellationToken);
    }

    public Task ReleaseAllMouseButtonsAsync(CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(ReleaseAllMouseButtonsCore, cancellationToken);
    }

    public Task ScrollMouseAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default)
    {
        if (deltaX == 0 && deltaY == 0)
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() => ScrollMouseCore(deltaX, deltaY), cancellationToken);
    }

    public void Dispose()
    {
        try
        {
            ReleaseAllKeysAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort only during shutdown.
        }

        try
        {
            ReleaseAllMouseButtonsAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort only during shutdown.
        }

        _executionQueue.Dispose();
    }

    private async Task EnqueueAsync(Action action, CancellationToken cancellationToken)
    {
        await _executionQueue.EnqueueAsync(action, cancellationToken);
    }

    private static void MoveMouseCore(int dx, int dy)
    {
        var inputs = new[]
        {
            CreateMouseMoveInput(dx, dy),
        };

        SendInputs(inputs);
    }

    private void ClickMouseCore(string button, int repeat)
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

    private void SetMouseButtonStateCore(string button, bool isDown)
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

    private void ReleaseAllMouseButtonsCore()
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

    private static void ScrollMouseCore(int deltaX, int deltaY)
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
