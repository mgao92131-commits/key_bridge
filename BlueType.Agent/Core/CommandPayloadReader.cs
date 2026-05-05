using System.Text.Json;

namespace BlueType.Agent.Core;

internal static class CommandPayloadReader
{
    public static string GetRequiredString(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Missing string payload property: {propertyName}");
        }

        return property.GetString() ?? string.Empty;
    }

    public static IReadOnlyList<string> GetRequiredStringArray(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Missing array payload property: {propertyName}");
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"Payload array contains non-string value: {propertyName}");
            }

            values.Add(item.GetString() ?? string.Empty);
        }

        return values;
    }

    public static int GetRequiredInt(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidOperationException($"Missing integer payload property: {propertyName}");
        }

        if (!property.TryGetInt32(out var value))
        {
            throw new InvalidOperationException($"Invalid integer payload property: {propertyName}");
        }

        return value;
    }

    public static int GetOptionalInt(JsonElement payload, string propertyName, int defaultValue)
    {
        if (!payload.TryGetProperty(propertyName, out var property))
        {
            return defaultValue;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            throw new InvalidOperationException($"Invalid integer payload property: {propertyName}");
        }

        return value;
    }
}
