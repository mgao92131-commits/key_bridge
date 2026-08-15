using BlueType.Agent.Application.Shortcuts;

namespace BlueType.Agent.Application.Ports;

internal interface IShortcutProfileRepository
{
    IReadOnlyList<ShortcutProfileDefinition> Load();
}
