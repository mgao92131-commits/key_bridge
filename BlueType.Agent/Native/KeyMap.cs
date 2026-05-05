namespace BlueType.Agent.Native;

internal static class KeyMap
{
    private static readonly IReadOnlyDictionary<string, KeyDefinition> NamedValues = new Dictionary<string, KeyDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["ENTER"] = new(0x0D),
        ["ESC"] = new(0x1B),
        ["TAB"] = new(0x09),
        ["BACKSPACE"] = new(0x08),
        ["SPACE"] = new(0x20),
        ["LEFT"] = new(0x25, IsExtended: true),
        ["UP"] = new(0x26, IsExtended: true),
        ["RIGHT"] = new(0x27, IsExtended: true),
        ["DOWN"] = new(0x28, IsExtended: true),
        ["HOME"] = new(0x24, IsExtended: true),
        ["END"] = new(0x23, IsExtended: true),
        ["DELETE"] = new(0x2E, IsExtended: true),
        ["INSERT"] = new(0x2D, IsExtended: true),
        ["PAGEUP"] = new(0x21, IsExtended: true),
        ["PAGEDOWN"] = new(0x22, IsExtended: true),
        ["CTRL"] = new(0x11, IsModifier: true),
        ["CONTROL"] = new(0x11, IsModifier: true),
        ["SHIFT"] = new(0x10, IsModifier: true),
        ["ALT"] = new(0x12, IsModifier: true),
        ["WIN"] = new(0x5B, IsExtended: true, IsModifier: true),
        ["LWIN"] = new(0x5B, IsExtended: true, IsModifier: true),
        ["RWIN"] = new(0x5C, IsExtended: true, IsModifier: true),
        ["F1"] = new(0x70),
        ["F2"] = new(0x71),
        ["F3"] = new(0x72),
        ["F4"] = new(0x73),
        ["F5"] = new(0x74),
        ["F6"] = new(0x75),
        ["F7"] = new(0x76),
        ["F8"] = new(0x77),
        ["F9"] = new(0x78),
        ["F10"] = new(0x79),
        ["F11"] = new(0x7A),
        ["F12"] = new(0x7B),
    };

    public static bool TryResolve(string key, out KeyDefinition definition)
    {
        if (NamedValues.TryGetValue(Normalize(key), out definition))
        {
            return true;
        }

        if (key.Length == 1)
        {
            var ch = char.ToUpperInvariant(key[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                definition = new((ushort)ch);
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                definition = new((ushort)ch);
                return true;
            }
        }

        definition = default;
        return false;
    }

    public static bool IsModifierPressed(string key)
    {
        if (!TryResolve(key, out var definition) || !definition.IsModifier)
        {
            return false;
        }

        return (Win32.GetAsyncKeyState(definition.VirtualKey) & 0x8000) != 0;
    }

    private static string Normalize(string key)
    {
        return key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("+", string.Empty, StringComparison.Ordinal)
            .Trim();
    }
}

internal readonly record struct KeyDefinition(ushort VirtualKey, bool IsExtended = false, bool IsModifier = false);
