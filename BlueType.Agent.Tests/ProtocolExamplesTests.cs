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

    private static string FindExamplesDirectory()
    {
        return Path.Combine(FindSpecDirectory(), "examples");
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
