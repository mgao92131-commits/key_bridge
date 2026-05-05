using BlueType.Agent.Core;
using BlueType.Agent.Models;
using BlueType.Agent.Tests.TestHelpers;

namespace BlueType.Agent.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task RequestApprovalAsync_ReturnsTransientAuthorization_WhenAllowOnceIsSelected()
    {
        using var sandbox = new DeviceRegistrySandbox();
        var registry = new DeviceRegistry(sandbox.SettingsFilePath);
        var authService = new AuthService(registry, (_, _) => Task.FromResult(AuthPromptDecision.AllowOnce));

        var result = await authService.RequestApprovalAsync(
            new HelloInfo("device-1", "Pixel", "1.0.0"),
            "10.0.0.2:24862",
            "wifi",
            CancellationToken.None);

        Assert.True(result.IsAuthorized);
        Assert.False(result.PersistToken);
        Assert.Null(result.Token);
        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public async Task RequestApprovalAsync_PersistsAuthorizedDevice_AndKnownDeviceCanReconnectWithToken()
    {
        using var sandbox = new DeviceRegistrySandbox();
        var registry = new DeviceRegistry(sandbox.SettingsFilePath);
        var authService = new AuthService(registry, (_, _) => Task.FromResult(AuthPromptDecision.AlwaysAllow));

        var approval = await authService.RequestApprovalAsync(
            new HelloInfo("device-2", "Galaxy", "1.0.0"),
            "10.0.0.3:24862",
            "wifi",
            CancellationToken.None);

        Assert.True(approval.IsAuthorized);
        Assert.True(approval.PersistToken);
        Assert.False(string.IsNullOrWhiteSpace(approval.Token));

        var reconnect = authService.TryAuthorizeKnownDevice(
            new HelloInfo("device-2", "Galaxy S", "1.1.0"),
            approval.Token,
            "10.0.0.4:24862",
            "wifi");

        Assert.True(reconnect.IsAuthorized);
        Assert.True(reconnect.PersistToken);
        Assert.Equal(approval.Token, reconnect.Token);

        var reloaded = new DeviceRegistry(sandbox.SettingsFilePath);
        Assert.True(reloaded.TryGet("device-2", out var device));
        Assert.Equal("Galaxy S", device.DeviceName);
        Assert.Equal("10.0.0.4", device.LastIp);
        Assert.Equal("wifi", device.LastTransport);
    }

    [Fact]
    public async Task RequestApprovalAsync_ReturnsUiUnavailable_WhenPromptThrows()
    {
        using var sandbox = new DeviceRegistrySandbox();
        var registry = new DeviceRegistry(sandbox.SettingsFilePath);
        var authService = new AuthService(registry, (_, _) => throw new InvalidOperationException("boom"));

        var result = await authService.RequestApprovalAsync(
            new HelloInfo("device-3", "Moto", "1.0.0"),
            "10.0.0.5:24862",
            "wifi",
            CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Equal("AUTH_UI_UNAVAILABLE", result.ErrorCode);
    }
}
