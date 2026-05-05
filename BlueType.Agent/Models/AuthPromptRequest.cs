namespace BlueType.Agent.Models;

internal sealed record AuthPromptRequest(
    AuthPromptMode Mode,
    string DeviceId,
    string DeviceName,
    string? RemoteAddress,
    string Transport,
    string? ActiveDeviceName = null,
    string? ActiveRemoteAddress = null,
    string? ActiveTransport = null);
