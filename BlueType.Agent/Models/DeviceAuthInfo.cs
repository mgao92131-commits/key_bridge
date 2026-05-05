namespace BlueType.Agent.Models;

internal sealed record DeviceAuthInfo(
    string DeviceId,
    string DeviceName,
    string? BluetoothAddress,
    string? LastIp,
    string? LastTransport,
    string TokenHash,
    DateTimeOffset LastSeenAt);
