using BlueType.Agent.Core;
using BlueType.Agent.Models;
using BlueType.Agent.Tests.TestHelpers;

namespace BlueType.Agent.Tests;

public sealed class DeviceRegistryTests
{
    [Fact]
    public void Constructor_LoadsEmptyRegistry_AndPreservesCorruptFile_WhenSettingsJsonIsInvalid()
    {
        using var sandbox = new DeviceRegistrySandbox();
        File.WriteAllText(sandbox.SettingsFilePath, "{ invalid json");

        var registry = new DeviceRegistry(sandbox.SettingsFilePath);

        Assert.Empty(registry.GetAll());
        Assert.True(File.Exists(sandbox.SettingsFilePath));
        Assert.Single(Directory.GetFiles(sandbox.DirectoryPath, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Upsert_PersistsDevice_AndCanReloadFromDisk()
    {
        using var sandbox = new DeviceRegistrySandbox();
        var registry = new DeviceRegistry(sandbox.SettingsFilePath);
        var device = CreateDevice("device-1", "Pixel");

        registry.Upsert(device);

        var reloaded = new DeviceRegistry(sandbox.SettingsFilePath);
        Assert.True(reloaded.TryGet("device-1", out var stored));
        Assert.Equal(device, stored);
    }

    [Fact]
    public void Upsert_LeavesNoTemporaryOrBackupFiles_AfterSuccessfulWrite()
    {
        using var sandbox = new DeviceRegistrySandbox();
        var registry = new DeviceRegistry(sandbox.SettingsFilePath);

        registry.Upsert(CreateDevice("device-2", "Galaxy"));

        Assert.True(File.Exists(sandbox.SettingsFilePath));
        Assert.Empty(Directory.GetFiles(sandbox.DirectoryPath, "settings.json.*.tmp"));
        Assert.Empty(Directory.GetFiles(sandbox.DirectoryPath, "settings.json.bak"));
    }

    private static DeviceAuthInfo CreateDevice(string deviceId, string deviceName)
    {
        return new DeviceAuthInfo(
            DeviceId: deviceId,
            DeviceName: deviceName,
            BluetoothAddress: null,
            LastIp: "127.0.0.1",
            LastTransport: "wifi",
            TokenHash: "sha256:TEST",
            LastSeenAt: DateTimeOffset.Parse("2026-04-22T00:00:00+00:00"));
    }
}
