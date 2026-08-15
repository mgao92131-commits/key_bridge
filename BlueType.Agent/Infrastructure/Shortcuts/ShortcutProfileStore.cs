using System.Text.Json;
using BlueType.Agent.Application.Shortcuts;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Protocol;

namespace BlueType.Agent.Infrastructure.Shortcuts;

internal static class ShortcutProfileStore
{
    public static IReadOnlyList<ShortcutProfileDefinition> Load()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlueType",
            "shortcut-profiles.json");

        if (!File.Exists(path))
        {
            try
            {
                SeedDefaultFile(path);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to create default shortcut profile file at {path}.", ex);
                return Array.Empty<ShortcutProfileDefinition>();
            }
        }

        try
        {
            var json = File.ReadAllText(path);
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
            AppLogger.Error($"Failed to load shortcut profiles from {path}. Android will use local defaults.", ex);
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

    private static void SeedDefaultFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(
            DefaultShortcutProfiles.CreateFileDocument(),
            new JsonSerializerOptions(JsonProtocol.SerializerOptions)
            {
                WriteIndented = true,
            });
        File.WriteAllText(path, json);
        AppLogger.Info($"Created default shortcut profile file at {path}.");
    }
}
