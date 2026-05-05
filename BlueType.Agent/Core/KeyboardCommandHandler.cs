using System.Text;
using BlueType.Protocol;

namespace BlueType.Agent.Core;

internal sealed class KeyboardCommandHandler : ICommandHandler
{
    private readonly InputInjector _inputInjector;

    public KeyboardCommandHandler(InputInjector inputInjector)
    {
        _inputInjector = inputInjector;
    }

    public async Task<Envelope?> TryHandleAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        switch (envelope.Type)
        {
            case Commands.TextInsert:
            {
                var text = CommandPayloadReader.GetRequiredString(envelope.Payload, "text");
                if (Encoding.UTF8.GetByteCount(text) > 8 * 1024)
                {
                    return JsonProtocol.CreateEnvelope(
                        envelope.Id,
                        Responses.Error,
                        new { code = "INVALID_PAYLOAD", message = "Text payload exceeds 8 KB." });
                }

                await _inputInjector.SendTextAsync(text, cancellationToken);
                AppLogger.Info($"Handled command: text_insert ({Encoding.UTF8.GetByteCount(text)} bytes).");
                return CreateAck(envelope.Id);
            }

            case Commands.KeyTap:
            {
                var key = CommandPayloadReader.GetRequiredString(envelope.Payload, "key");
                await _inputInjector.TapKeyAsync(key, cancellationToken);
                AppLogger.Info($"Handled command: key_tap ({key}).");
                return CreateAck(envelope.Id);
            }

            case Commands.KeyDown:
            {
                var key = CommandPayloadReader.GetRequiredString(envelope.Payload, "key");
                await _inputInjector.PressKeyAsync(key, cancellationToken);
                AppLogger.Info($"Handled command: key_down ({key}).");
                return CreateAck(envelope.Id);
            }

            case Commands.KeyUp:
            {
                var key = CommandPayloadReader.GetRequiredString(envelope.Payload, "key");
                await _inputInjector.ReleaseKeyAsync(key, cancellationToken);
                AppLogger.Info($"Handled command: key_up ({key}).");
                return CreateAck(envelope.Id);
            }

            case Commands.Combo:
            {
                var keys = CommandPayloadReader.GetRequiredStringArray(envelope.Payload, "keys");
                await _inputInjector.SendComboAsync(keys, cancellationToken);
                AppLogger.Info($"Handled command: combo ({string.Join("+", keys)}).");
                return CreateAck(envelope.Id);
            }

            default:
                return null;
        }
    }

    private static Envelope CreateAck(string requestId)
    {
        return JsonProtocol.CreateEnvelope(requestId, Responses.Ack, new { ok = true });
    }
}
