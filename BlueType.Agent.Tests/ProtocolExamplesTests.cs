using System.Reflection;
using System.Text.Json;
using BlueType.Protocol;

namespace BlueType.Agent.Tests;

public sealed class ProtocolExamplesTests
{
    public static IEnumerable<object[]> ExampleFiles()
    {
        foreach (var path in Directory.EnumerateFiles(FindExamplesDirectory(), "*.json").OrderBy(Path.GetFileName))
        {
            yield return new object[] { path };
        }
    }

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task ProtocolExample_DecodesAndRoundTripsThroughFrameCodec(string path)
    {
        var manifest = await LoadManifestAsync();
        var knownTypes = manifest.Commands.Concat(manifest.Responses).ToHashSet(StringComparer.Ordinal);
        var knownErrorCodes = manifest.ErrorCodes.ToHashSet(StringComparer.Ordinal);
        var json = await File.ReadAllTextAsync(path);
        var envelope = JsonSerializer.Deserialize<Envelope>(json, JsonProtocol.SerializerOptions);

        Assert.NotNull(envelope);
        Assert.Equal(manifest.Version, envelope.V);
        Assert.False(string.IsNullOrWhiteSpace(envelope.Id));
        Assert.Contains(envelope.Type, knownTypes);
        Assert.Equal(JsonValueKind.Object, envelope.Payload.ValueKind);

        if (string.Equals(envelope.Type, Responses.Error, StringComparison.Ordinal))
        {
            Assert.True(envelope.Payload.TryGetProperty("code", out var code));
            Assert.Equal(JsonValueKind.String, code.ValueKind);
            var errorCode = code.GetString();
            Assert.NotNull(errorCode);
            Assert.Contains(errorCode, knownErrorCodes);
        }

        using var stream = new MemoryStream();
        await FrameCodec.WriteAsync(stream, envelope);
        stream.Position = 0;

        var roundTripped = await FrameCodec.ReadAsync(stream);
        Assert.NotNull(roundTripped);
        AssertEnvelopeEquivalent(envelope, roundTripped);
    }

    [Fact]
    public async Task ProtocolManifest_RequiresFixtureCoverageForEveryMessageTypeAndErrorCode()
    {
        var manifest = await LoadManifestAsync();
        var expectedTypes = manifest.Commands.Concat(manifest.Responses).ToHashSet(StringComparer.Ordinal);
        var coveredTypes = new HashSet<string>(StringComparer.Ordinal);
        var coveredErrorCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(FindExamplesDirectory(), "*.json"))
        {
            var envelope = Assert.IsType<Envelope>(JsonSerializer.Deserialize<Envelope>(
                await File.ReadAllTextAsync(path),
                JsonProtocol.SerializerOptions));

            Assert.Contains(envelope.Type, expectedTypes);
            coveredTypes.Add(envelope.Type);

            if (string.Equals(envelope.Type, Responses.Error, StringComparison.Ordinal) &&
                envelope.Payload.TryGetProperty("code", out var code) &&
                code.ValueKind == JsonValueKind.String)
            {
                coveredErrorCodes.Add(code.GetString()!);
            }
        }

        foreach (var type in expectedTypes)
        {
            Assert.Contains(type, coveredTypes);
        }

        foreach (var errorCode in manifest.ErrorCodes)
        {
            Assert.Contains(errorCode, coveredErrorCodes);
        }
    }

    [Fact]
    public async Task CSharpProtocolConstants_MatchProtocolManifest()
    {
        var manifest = await LoadManifestAsync();

        Assert.Equal(
            manifest.Commands.OrderBy(value => value, StringComparer.Ordinal),
            GetProtocolConstants(typeof(Commands)));
        Assert.Equal(
            manifest.Responses.OrderBy(value => value, StringComparer.Ordinal),
            GetProtocolConstants(typeof(Responses)));
    }

    [Fact]
    public async Task InvalidProtocolExamples_AreRejectedByV1Contract()
    {
        var manifest = await LoadManifestAsync();

        foreach (var path in Directory.EnumerateFiles(FindInvalidDirectory(), "*.json").OrderBy(Path.GetFileName))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.False(IsValidEnvelope(document.RootElement, manifest), path);
        }
    }

    private sealed record ProtocolManifest(
        int Version,
        string[] Commands,
        string[] Responses,
        string[] ErrorCodes);

    private static async Task<ProtocolManifest> LoadManifestAsync()
    {
        var manifest = JsonSerializer.Deserialize<ProtocolManifest>(
            await File.ReadAllTextAsync(Path.Combine(FindSpecDirectory(), "protocol-v1.json")),
            JsonProtocol.SerializerOptions);
        return Assert.IsType<ProtocolManifest>(manifest);
    }

    private static string[] GetProtocolConstants(Type constantsType)
    {
        return constantsType
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsValidEnvelope(JsonElement root, ProtocolManifest manifest)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("v", out var version) ||
            version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var versionValue) ||
            versionValue != manifest.Version ||
            !HasNonEmptyString(root, "id") ||
            !HasNonEmptyString(root, "type") ||
            !root.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var type = root.GetProperty("type").GetString()!;
        if (!manifest.Commands.Concat(manifest.Responses).Contains(type, StringComparer.Ordinal))
        {
            return false;
        }

        return type switch
        {
            Commands.Hello => HasNonEmptyString(payload, "deviceId") && HasNonEmptyString(payload, "deviceName"),
            Commands.TextInsert => HasString(payload, "text"),
            Commands.KeyTap or Commands.KeyDown or Commands.KeyUp => HasNonEmptyString(payload, "key"),
            Commands.Combo => HasStringArray(payload, "keys"),
            Commands.MouseMove => HasInt(payload, "dx") && HasInt(payload, "dy"),
            Commands.MouseButton => HasNonEmptyString(payload, "button") && HasOneOf(payload, "action", "down", "up"),
            Commands.MouseClick => HasNonEmptyString(payload, "button") &&
                                   (!payload.TryGetProperty("repeat", out var repeat) || IsInt(repeat)),
            Commands.MouseScroll => OptionalInt(payload, "deltaX") && OptionalInt(payload, "deltaY"),
            Commands.ClipboardSet => HasString(payload, "text"),
            Commands.ClipboardGet or Commands.Ping or Responses.Pong => true,
            Responses.Ack => HasBoolean(payload, "ok"),
            Responses.Error => HasErrorPayload(payload, manifest),
            Responses.AuthPending => HasInt(payload, "timeoutSec") && HasString(payload, "message"),
            Responses.AuthResult => HasBoolean(payload, "ok") &&
                                    HasBoolean(payload, "persistToken") &&
                                    HasBoolean(payload, "trusted"),
            Responses.ClipboardValue => HasString(payload, "text"),
            Responses.ShortcutProfile => HasNullableString(payload, "name") && HasNullableObject(payload, "profile"),
            _ => false,
        };
    }

    private static bool HasErrorPayload(JsonElement payload, ProtocolManifest manifest)
    {
        return HasString(payload, "message") &&
               payload.TryGetProperty("code", out var code) &&
               code.ValueKind == JsonValueKind.String &&
               code.GetString() is string codeValue &&
               manifest.ErrorCodes.Contains(codeValue, StringComparer.Ordinal);
    }

    private static bool HasNonEmptyString(JsonElement element, string propertyName)
    {
        return HasString(element, propertyName) && !string.IsNullOrWhiteSpace(element.GetProperty(propertyName).GetString());
    }

    private static bool HasString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String;
    }

    private static bool HasNullableString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.String or JsonValueKind.Null;
    }

    private static bool HasNullableObject(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.Object or JsonValueKind.Null;
    }

    private static bool HasStringArray(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Array &&
               property.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String);
    }

    private static bool HasInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && IsInt(property);
    }

    private static bool OptionalInt(JsonElement element, string propertyName)
    {
        return !element.TryGetProperty(propertyName, out var property) || IsInt(property);
    }

    private static bool IsInt(JsonElement property)
    {
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out _);
    }

    private static bool HasBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.True or JsonValueKind.False;
    }

    private static bool HasOneOf(JsonElement element, string propertyName, params string[] values)
    {
        return HasString(element, propertyName) &&
               element.GetProperty(propertyName).GetString() is string value &&
               values.Contains(value, StringComparer.Ordinal);
    }

    private static string FindExamplesDirectory()
    {
        return Path.Combine(FindSpecDirectory(), "examples");
    }

    private static string FindInvalidDirectory()
    {
        return Path.Combine(FindSpecDirectory(), "invalid");
    }

    private static string FindSpecDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "protocol", "spec");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find protocol/spec from the test output directory.");
    }

    private static void AssertEnvelopeEquivalent(Envelope expected, Envelope actual)
    {
        Assert.Equal(expected.V, actual.V);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Token, actual.Token);
        Assert.True(JsonElementDeepEquals(expected.Payload, actual.Payload));
    }

    private static bool JsonElementDeepEquals(JsonElement left, JsonElement right)
    {
        using var leftDocument = JsonDocument.Parse(left.GetRawText());
        using var rightDocument = JsonDocument.Parse(right.GetRawText());
        return JsonElementDeepEqualsCore(leftDocument.RootElement, rightDocument.RootElement);
    }

    private static bool JsonElementDeepEqualsCore(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Object => ObjectsDeepEqual(left, right),
            JsonValueKind.Array => ArraysDeepEqual(left, right),
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null => true,
            _ => left.GetRawText() == right.GetRawText(),
        };
    }

    private static bool ObjectsDeepEqual(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        var rightProperties = right.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        if (leftProperties.Length != rightProperties.Length)
        {
            return false;
        }

        for (var i = 0; i < leftProperties.Length; i++)
        {
            if (!string.Equals(leftProperties[i].Name, rightProperties[i].Name, StringComparison.Ordinal))
            {
                return false;
            }

            if (!JsonElementDeepEqualsCore(leftProperties[i].Value, rightProperties[i].Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ArraysDeepEqual(JsonElement left, JsonElement right)
    {
        var leftItems = left.EnumerateArray().ToArray();
        var rightItems = right.EnumerateArray().ToArray();
        if (leftItems.Length != rightItems.Length)
        {
            return false;
        }

        for (var i = 0; i < leftItems.Length; i++)
        {
            if (!JsonElementDeepEqualsCore(leftItems[i], rightItems[i]))
            {
                return false;
            }
        }

        return true;
    }
}
