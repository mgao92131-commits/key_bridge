using System.Text.Json;
using BlueType.Agent.Application.Shortcuts;
using BlueType.Agent.Application.Ports;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Protocol;

namespace BlueType.Agent.Infrastructure.Shortcuts;

internal sealed class JsonShortcutProfileRepository : IShortcutProfileRepository
{
    private readonly string _path;

    public JsonShortcutProfileRepository()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlueType",
            "shortcut-profiles.json"))
    {
    }

    internal JsonShortcutProfileRepository(string path)
    {
        _path = path;
    }

    public IReadOnlyList<ShortcutProfileDefinition> Load()
    {
        if (!File.Exists(_path))
        {
            try
            {
                SeedDefaultFile();
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to create default shortcut profile file at {_path}.", ex);
                return Array.Empty<ShortcutProfileDefinition>();
            }
        }

        try
        {
            var json = File.ReadAllText(_path);
            var document = JsonSerializer.Deserialize<ShortcutProfileFile>(json, JsonProtocol.SerializerOptions);
            if (document?.Profiles is null)
            {
                return Array.Empty<ShortcutProfileDefinition>();
            }

            var profiles = document.Profiles
                .Where(profile => profile.Profile.ValueKind is JsonValueKind.Object)
                .Select(profile => new ShortcutProfileDefinition(
                    profile.Id ?? string.Empty,
                    profile.Name ?? profile.Id ?? string.Empty,
                    new ShortcutProfileMatch(
                        profile.Match?.WindowsProcesses ?? [],
                        profile.Match?.MacBundleIds ?? []),
                    profile.Profile.Clone()))
                .Where(profile => !string.IsNullOrWhiteSpace(profile.Id))
                .ToArray();
            if (!profiles.Any(profile => string.Equals(profile.Id, "default", StringComparison.OrdinalIgnoreCase)))
            {
                profiles = profiles.Append(DefaultShortcutProfiles.CreateDefaultDefinition()).ToArray();
            }

            return profiles;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to load shortcut profiles from {_path}. Android will use local defaults.", ex);
            return Array.Empty<ShortcutProfileDefinition>();
        }
    }

    private sealed class ShortcutProfileFile
    {
        public List<ShortcutProfileDto> Profiles { get; set; } = [];
    }

    private sealed class ShortcutProfileDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public ShortcutProfileMatchDto? Match { get; set; }
        public JsonElement Profile { get; set; }
    }

    private sealed class ShortcutProfileMatchDto
    {
        public List<string> WindowsProcesses { get; set; } = [];
        public List<string> MacBundleIds { get; set; } = [];
    }

    private void SeedDefaultFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(
            DefaultShortcutProfiles.CreateFileDocument(),
            new JsonSerializerOptions(JsonProtocol.SerializerOptions)
            {
                WriteIndented = true,
            });
        File.WriteAllText(_path, json);
        AppLogger.Info($"Created default shortcut profile file at {_path}.");
    }
}
