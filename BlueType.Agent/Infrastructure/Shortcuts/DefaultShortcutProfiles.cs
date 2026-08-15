using BlueType.Agent.Application.Shortcuts;
using BlueType.Protocol;

namespace BlueType.Agent.Infrastructure.Shortcuts;

internal static class DefaultShortcutProfiles
{
    public static object CreateFileDocument() => new
    {
        profiles = new object[]
        {
            new
            {
                id = "default",
                name = "Default",
                match = new
                {
                    windowsProcesses = Array.Empty<string>(),
                    macBundleIds = Array.Empty<string>(),
                },
                profile = DefaultProfile(),
            },
            new
            {
                id = "windows-terminal",
                name = "Terminal",
                match = new
                {
                    windowsProcesses = new[] { "WindowsTerminal", "wt", "cmd", "powershell", "pwsh" },
                    macBundleIds = Array.Empty<string>(),
                },
                profile = WindowsTerminalProfile(),
            },
        },
    };

    public static ShortcutProfileDefinition CreateDefaultDefinition() => new(
        "default",
        "Default",
        new ShortcutProfileMatch([], []),
        JsonProtocol.ToElement(DefaultProfile()).Clone());

    private static object DefaultProfile() => new
    {
        leftRail = Rail(Combo("SHIFT", "TAB"), KeyTap("TAB"), "ALT"),
        rightRail = Rail(Combo("SHIFT", "TAB"), KeyTap("TAB"), "CTRL"),
        bottomRail = Rail(KeyTap("LEFT"), KeyTap("RIGHT"), "WIN", "CTRL"),
        customButtons = new[]
        {
            Button("copy", "COPY", Combo("CTRL", "C")),
            Button("paste", "PASTE", Combo("CTRL", "V")),
            Button("cut", "CUT", Combo("CTRL", "X")),
            Button("undo", "UNDO", Combo("CTRL", "Z")),
            Button("redo", "REDO", Combo("CTRL", "Y")),
            Button("all", "ALL", Combo("CTRL", "A")),
            Button("save", "SAVE", Combo("CTRL", "S")),
            Button("find", "FIND", Combo("CTRL", "F")),
        },
    };

    private static object WindowsTerminalProfile() => new
    {
        leftRail = Rail(Combo("SHIFT", "TAB"), KeyTap("TAB"), "ALT"),
        rightRail = Rail(Combo("SHIFT", "TAB"), KeyTap("TAB"), "CTRL"),
        bottomRail = Rail(KeyTap("LEFT"), KeyTap("RIGHT"), "WIN", "CTRL"),
        customButtons = new[]
        {
            Button("copy", "COPY", Combo("CTRL", "SHIFT", "C")),
            Button("paste", "PASTE", Combo("CTRL", "SHIFT", "V")),
            Button("new_tab", "NEW TAB", Combo("CTRL", "SHIFT", "T")),
            Button("prev_tab", "PREV TAB", Combo("CTRL", "SHIFT", "TAB")),
            Button("next_tab", "NEXT TAB", Combo("CTRL", "TAB")),
            Button("interrupt", "INT", Combo("CTRL", "C")),
            Button("clear", "CLEAR", Combo("CTRL", "L")),
            Button("find", "FIND", Combo("CTRL", "SHIFT", "F")),
        },
    };

    private static object Rail(object primaryAction, object secondaryAction, params string[] stickyModifiers) => new
    {
        primaryAction,
        secondaryAction,
        stickyModifiers,
        stickyDurationMs = 600,
    };

    private static object Button(string id, string label, object action) => new
    {
        id,
        label,
        action,
    };

    private static object KeyTap(string key) => new
    {
        kind = "key_tap",
        key,
    };

    private static object Combo(params string[] keys) => new
    {
        kind = "combo",
        keys,
    };
}
