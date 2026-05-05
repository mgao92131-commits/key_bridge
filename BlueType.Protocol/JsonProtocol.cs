using System.Text.Json;

namespace BlueType.Protocol;

public static class JsonProtocol
{
    public static readonly JsonElement EmptyObject = JsonSerializer.SerializeToElement(new { });

    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static JsonElement ToElement<T>(T payload)
    {
        return JsonSerializer.SerializeToElement(payload, SerializerOptions);
    }

    public static Envelope CreateEnvelope(string id, string type, object? payload = null, string? token = null)
    {
        return new Envelope(
            V: 1,
            Id: id,
            Type: type,
            Token: token,
            Payload: payload is null ? EmptyObject : ToElement(payload));
    }
}
