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
    private readonly Dictionary<ushort, KeyDefinition> _pressedKeys = new();
    private readonly HashSet<string> _pressedMouseButtons = new(StringComparer.OrdinalIgnoreCase);

    public InputInjector()
    {
        _executionQueue = new InputExecutionQueue();
    }

    public Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() => SendTextCore(text), cancellationToken);
    }

    public Task TapKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => TapKeyCore(key), cancellationToken);
    }

    public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => PressKeyCore(key), cancellationToken);
    }

    public Task ReleaseKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => ReleaseKeyCore(key), cancellationToken);
    }

    public Task ReleaseAllKeysAsync(CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(ReleaseAllKeysCore, cancellationToken);
    }

    public Task SendComboAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() => SendComboCore(keys), cancellationToken);
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

    private static void SendTextCore(string text)
    {
        var inputs = new List<Win32.INPUT>(text.Length * 2);

        foreach (var ch in text)
        {
            inputs.Add(CreateUnicodeInput(ch, keyUp: false));
            inputs.Add(CreateUnicodeInput(ch, keyUp: true));
        }

        SendInputs(inputs);
    }

    private void TapKeyCore(string key)
    {
        var definition = ResolveKey(key);
        if (_pressedKeys.ContainsKey(definition.VirtualKey))
        {
            throw new InvalidOperationException($"Key is already pressed: {key}");
        }

        if (definition.IsModifier && KeyMap.IsModifierPressed(key))
        {
            throw new InvalidOperationException($"Modifier key is already pressed: {key}");
        }

        var inputs = new[]
        {
            CreateVirtualKeyInput(definition, keyUp: false),
            CreateVirtualKeyInput(definition, keyUp: true),
        };

        SendInputs(inputs);
    }

    private void PressKeyCore(string key)
    {
        var definition = ResolveKey(key);
        if (_pressedKeys.ContainsKey(definition.VirtualKey))
        {
            return;
        }

        if (definition.IsModifier && KeyMap.IsModifierPressed(key))
        {
            throw new InvalidOperationException($"Modifier key is already pressed: {key}");
        }

        SendInputs([CreateVirtualKeyInput(definition, keyUp: false)]);
        _pressedKeys[definition.VirtualKey] = definition;
    }

    private void ReleaseKeyCore(string key)
    {
        var definition = ResolveKey(key);
        if (!_pressedKeys.TryGetValue(definition.VirtualKey, out var pressedDefinition))
        {
            return;
        }

        SendInputs([CreateVirtualKeyInput(pressedDefinition, keyUp: true)]);
        _pressedKeys.Remove(definition.VirtualKey);
    }

    private void ReleaseAllKeysCore()
    {
        if (_pressedKeys.Count == 0)
        {
            return;
        }

        var inputs = _pressedKeys.Values
            .OrderByDescending(definition => definition.VirtualKey)
            .Select(definition => CreateVirtualKeyInput(definition, keyUp: true))
            .ToArray();

        SendInputs(inputs);
        _pressedKeys.Clear();
    }

    private void SendComboCore(IReadOnlyList<string> keys)
    {
        var resolved = keys.Select(ResolveKey).ToArray();
        var modifiers = resolved.Where(definition => definition.IsModifier).ToArray();
        var newModifiers = modifiers
            .Where(definition => !_pressedKeys.ContainsKey(definition.VirtualKey))
            .ToArray();

        foreach (var modifier in newModifiers)
        {
            if ((Win32.GetAsyncKeyState(modifier.VirtualKey) & 0x8000) != 0)
            {
                throw new InvalidOperationException($"Modifier key is already pressed: {modifier.VirtualKey}");
            }
        }

        var mainKeys = resolved.Where(definition => !definition.IsModifier).ToArray();
        if (mainKeys.Length == 0)
        {
            throw new InvalidOperationException("Combo must include at least one non-modifier key.");
        }

        foreach (var mainKey in mainKeys)
        {
            if (_pressedKeys.ContainsKey(mainKey.VirtualKey))
            {
                throw new InvalidOperationException($"Key is already pressed: {mainKey.VirtualKey}");
            }
        }

        var inputs = new List<Win32.INPUT>(resolved.Length * 2);
        foreach (var modifier in newModifiers)
        {
            inputs.Add(CreateVirtualKeyInput(modifier, keyUp: false));
        }

        foreach (var mainKey in mainKeys)
        {
            inputs.Add(CreateVirtualKeyInput(mainKey, keyUp: false));
            inputs.Add(CreateVirtualKeyInput(mainKey, keyUp: true));
        }

        foreach (var modifier in newModifiers.Reverse())
        {
            inputs.Add(CreateVirtualKeyInput(modifier, keyUp: true));
        }

        SendInputs(inputs);
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

    private static KeyDefinition ResolveKey(string key)
    {
        if (!KeyMap.TryResolve(key, out var definition))
        {
            throw new InvalidOperationException($"Unsupported key: {key}");
        }

        return definition;
    }

    private static Win32.INPUT CreateUnicodeInput(char ch, bool keyUp)
    {
        return new Win32.INPUT
        {
            type = Win32.InputKeyboard,
            U = new Win32.InputUnion
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = Win32.KeyEventFUnicode | (keyUp ? Win32.KeyEventFKeyUp : 0),
                },
            },
        };
    }

    private static Win32.INPUT CreateVirtualKeyInput(KeyDefinition definition, bool keyUp)
    {
        var flags = definition.IsExtended ? Win32.KeyEventFExtendedKey : 0;
        if (keyUp)
        {
            flags |= Win32.KeyEventFKeyUp;
        }

        return new Win32.INPUT
        {
            type = Win32.InputKeyboard,
            U = new Win32.InputUnion
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = definition.VirtualKey,
                    wScan = 0,
                    dwFlags = flags,
                },
            },
        };
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
