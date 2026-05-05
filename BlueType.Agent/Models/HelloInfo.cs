namespace BlueType.Agent.Models;

internal sealed record HelloInfo(
    string DeviceId,
    string DeviceName,
    string? AppVersion);
