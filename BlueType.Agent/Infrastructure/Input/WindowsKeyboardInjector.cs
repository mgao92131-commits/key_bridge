using System.Runtime.InteropServices;
using BlueType.Agent.Native;

namespace BlueType.Agent.Infrastructure.Input;

internal sealed class WindowsKeyboardInjector
{
    private readonly Dictionary<ushort, KeyDefinition> _pressedKeys = new();

    public void SendText(string text)
    {
        var inputs = new List<Win32.INPUT>(text.Length * 2);

        foreach (var ch in text)
        {
            inputs.Add(CreateUnicodeInput(ch, keyUp: false));
            inputs.Add(CreateUnicodeInput(ch, keyUp: true));
        }

        SendInputs(inputs);
    }

    public void TapKey(string key)
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

    public void PressKey(string key)
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

    public void ReleaseKey(string key)
    {
        var definition = ResolveKey(key);
        if (!_pressedKeys.TryGetValue(definition.VirtualKey, out var pressedDefinition))
        {
            return;
        }

        SendInputs([CreateVirtualKeyInput(pressedDefinition, keyUp: true)]);
        _pressedKeys.Remove(definition.VirtualKey);
    }

    public void ReleaseAll()
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

    public void SendCombo(IReadOnlyList<string> keys)
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

    private static void SendInputs(IReadOnlyList<Win32.INPUT> inputs)
    {
        var sent = Win32.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Win32.INPUT>());
        if (sent != inputs.Count)
        {
            throw new InvalidOperationException(
                $"SendInput failed or was blocked. sent={sent} expected={inputs.Count} win32={Marshal.GetLastWin32Error()}");
        }
    }
}
