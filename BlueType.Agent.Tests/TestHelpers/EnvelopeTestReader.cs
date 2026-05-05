using BlueType.Protocol;

namespace BlueType.Agent.Tests.TestHelpers;

internal static class EnvelopeTestReader
{
    public static Task<IReadOnlyList<Envelope>> ReadAllAsync(byte[] bytes)
    {
        return ReadAllAsync(new MemoryStream(bytes, writable: false));
    }

    public static async Task<IReadOnlyList<Envelope>> ReadAllAsync(MemoryStream stream)
    {
        stream.Position = 0;
        var envelopes = new List<Envelope>();

        while (stream.Position < stream.Length)
        {
            var envelope = await FrameCodec.ReadAsync(stream);
            if (envelope is null)
            {
                break;
            }

            envelopes.Add(envelope);
        }

        return envelopes;
    }

    public static string GetString(Envelope envelope, string propertyName)
    {
        return envelope.Payload.GetProperty(propertyName).GetString() ?? string.Empty;
    }

    public static bool GetBoolean(Envelope envelope, string propertyName)
    {
        return envelope.Payload.GetProperty(propertyName).GetBoolean();
    }
}
