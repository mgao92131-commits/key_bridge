using System.Text.Json;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Models;
using BlueType.Protocol;

namespace BlueType.Agent.Infrastructure.Persistence;

internal interface IAuthorizedDeviceRepository
{
    IReadOnlyList<DeviceAuthInfo> Load();

    void Save(IEnumerable<DeviceAuthInfo> devices);
}

internal sealed class JsonAuthorizedDeviceRepository : IAuthorizedDeviceRepository
{
    private readonly string _settingsFilePath;

    public JsonAuthorizedDeviceRepository(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
    }

    public IReadOnlyList<DeviceAuthInfo> Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return Array.Empty<DeviceAuthInfo>();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<DeviceAuthInfo>();
            }

            var document = JsonSerializer.Deserialize<SettingsDocument>(json, JsonProtocol.SerializerOptions);
            return document?.Devices ?? [];
        }
        catch (Exception ex)
        {
            var preservedPath = TryPreserveCorruptSettings(_settingsFilePath);
            var message = preservedPath is null
                ? $"Failed to load device registry from {_settingsFilePath}. Falling back to an empty registry."
                : $"Failed to load device registry from {_settingsFilePath}. Preserved corrupt file at {preservedPath} and fell back to an empty registry.";
            AppLogger.Error(message, ex);
            return Array.Empty<DeviceAuthInfo>();
        }
    }

    public void Save(IEnumerable<DeviceAuthInfo> devices)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new SettingsDocument
        {
            Devices = devices.OrderBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase).ToList(),
        };

        var json = JsonSerializer.Serialize(document, JsonProtocol.SerializerOptions);
        var tempPath = $"{_settingsFilePath}.{Guid.NewGuid():N}.tmp";
        var backupPath = $"{_settingsFilePath}.bak";

        try
        {
            File.WriteAllText(tempPath, json);

            if (File.Exists(_settingsFilePath))
            {
                File.Replace(tempPath, _settingsFilePath, backupPath, ignoreMetadataErrors: true);
                TryDeleteIfExists(backupPath);
            }
            else
            {
                File.Move(tempPath, _settingsFilePath);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to persist device registry to {_settingsFilePath}.", ex);
            TryDeleteIfExists(tempPath);
            throw;
        }
    }

    private static string? TryPreserveCorruptSettings(string settingsFilePath)
    {
        try
        {
            var preservedPath = $"{settingsFilePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(settingsFilePath, preservedPath, overwrite: false);
            return preservedPath;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private sealed class SettingsDocument
    {
        public List<DeviceAuthInfo> Devices { get; init; } = [];
    }
}
