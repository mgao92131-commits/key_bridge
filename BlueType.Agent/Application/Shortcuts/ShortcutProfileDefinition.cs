using System.Text.Json;

namespace BlueType.Agent.Application.Shortcuts;

internal sealed record ShortcutProfileDefinition(
    string Id,
    string Name,
    ShortcutProfileMatch Match,
    JsonElement Profile);

internal sealed record ShortcutProfileMatch(
    IReadOnlyList<string> WindowsProcesses,
    IReadOnlyList<string> MacBundleIds);
