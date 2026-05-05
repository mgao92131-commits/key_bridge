using System.Text.Json;
using BlueType.Protocol;

namespace BlueType.Agent.Tests;

public sealed class ProtocolExamplesTests
{
    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
    {
        Commands.Hello,
        Commands.TextInsert,
        Commands.KeyTap,
        Commands.KeyDown,
        Commands.KeyUp,
        Commands.Combo,
        Commands.MouseMove,
        Commands.MouseButton,
        Commands.MouseClick,
        Commands.MouseScroll,
        Commands.ClipboardSet,
        Commands.ClipboardGet,
        Commands.Ping,
        Commands.Pong,
        Responses.Ack,
        Responses.Error,
        Responses.AuthPending,
        Responses.AuthResult,
        Responses.ClipboardValue,
        Responses.ShortcutProfile,
    };

    private static readonly HashSet<string> KnownErrorCodes = new(StringComparer.Ordinal)
    {
        "BUSY",
        "NOT_AUTHORIZED",
        "AUTH_TIMEOUT",
        "AUTH_UI_UNAVAILABLE",
        "INVALID_PAYLOAD",
        "SERVER_ERROR",
        "SESSION_REPLACED",
        "INPUT_BLOCKED",
        "CLIPBOARD_FAILED",
    };

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
        var json = await File.ReadAllTextAsync(path);
        var envelope = JsonSerializer.Deserialize<Envelope>(json, JsonProtocol.SerializerOptions);

        Assert.NotNull(envelope);
        Assert.Equal(1, envelope.V);
        Assert.False(string.IsNullOrWhiteSpace(envelope.Id));
        Assert.Contains(envelope.Type, KnownTypes);
        Assert.Equal(JsonValueKind.Object, envelope.Payload.ValueKind);

        if (string.Equals(envelope.Type, Responses.Error, StringComparison.Ordinal))
        {
            Assert.True(envelope.Payload.TryGetProperty("code", out var code));
            Assert.Equal(JsonValueKind.String, code.ValueKind);
            var errorCode = code.GetString();
            Assert.NotNull(errorCode);
            Assert.Contains(errorCode, KnownErrorCodes);
        }

        using var stream = new MemoryStream();
        await FrameCodec.WriteAsync(stream, envelope);
        stream.Position = 0;

        var roundTripped = await FrameCodec.ReadAsync(stream);
        Assert.NotNull(roundTripped);
        AssertEnvelopeEquivalent(envelope, roundTripped);
    }

    private static string FindExamplesDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "protocol", "spec", "examples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find protocol/spec/examples from the test output directory.");
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
