using System.Text.Json;

namespace BlueType.Protocol;

public sealed record Envelope(
    int V,
    string Id,
    string Type,
    string? Token,
    JsonElement Payload);
