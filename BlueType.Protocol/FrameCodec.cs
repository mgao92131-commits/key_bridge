using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace BlueType.Protocol;

public static class FrameCodec
{
    public const int MaxFrameSize = 64 * 1024;

    public static async Task WriteAsync(Stream stream, Envelope envelope, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(envelope, JsonProtocol.SerializerOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        if (payloadBytes.Length > MaxFrameSize)
        {
            throw new InvalidDataException("Frame too large.");
        }

        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, payloadBytes.Length);

        await stream.WriteAsync(lengthBytes, cancellationToken);
        await stream.WriteAsync(payloadBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<Envelope?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var lengthBytes = new byte[4];
        var bytesRead = await FillBufferAsync(stream, lengthBytes, cancellationToken);
        if (bytesRead == 0)
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
        if (length <= 0 || length > MaxFrameSize)
        {
            throw new InvalidDataException($"Invalid frame length: {length}");
        }

        var payloadBytes = new byte[length];
        await FillBufferAsync(stream, payloadBytes, cancellationToken);
        var json = Encoding.UTF8.GetString(payloadBytes);

        return JsonSerializer.Deserialize<Envelope>(json, JsonProtocol.SerializerOptions);
    }

    private static async Task<int> FillBufferAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
            if (read == 0)
            {
                if (totalRead == 0)
                {
                    return 0;
                }

                throw new EndOfStreamException("Unexpected end of stream while reading frame.");
            }

            totalRead += read;
        }

        return totalRead;
    }
}
