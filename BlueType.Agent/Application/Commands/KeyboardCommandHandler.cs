using System.Text;
using BlueType.Agent.Application.Ports;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Protocol;
using ProtocolCommands = BlueType.Protocol.Commands;

namespace BlueType.Agent.Application.Commands;

internal sealed class KeyboardCommandHandler : ICommandHandler
{
    private static readonly string[] CommandTypes =
    [
        ProtocolCommands.TextInsert,
        ProtocolCommands.KeyTap,
        ProtocolCommands.KeyDown,
        ProtocolCommands.KeyUp,
        ProtocolCommands.Combo,
    ];

    private readonly IInputService _inputService;

    public KeyboardCommandHandler(IInputService inputService)
    {
        _inputService = inputService;
    }

    public IReadOnlyCollection<string> SupportedCommands => CommandTypes;

    public async Task<Envelope> HandleAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        switch (envelope.Type)
        {
            case BlueType.Protocol.Commands.TextInsert:
            {
                var text = CommandPayloadReader.GetRequiredString(envelope.Payload, "text");
                if (Encoding.UTF8.GetByteCount(text) > 8 * 1024)
                {
                    return JsonProtocol.CreateEnvelope(
                        envelope.Id,
                        Responses.Error,
                        new { code = "INVALID_PAYLOAD", message = "Text payload exceeds 8 KB." });
                }

                await _inputService.SendTextAsync(text, cancellationToken);
                AppLogger.Info($"Handled command: text_insert ({Encoding.UTF8.GetByteCount(text)} bytes).");
                return CreateAck(envelope.Id);
            }

            case BlueType.Protocol.Commands.KeyTap:
            {
                var key = CommandPayloadReader.GetRequiredString(envelope.Payload, "key");
                await _inputService.TapKeyAsync(key, cancellationToken);
                AppLogger.Info($"Handled command: key_tap ({key}).");
                return CreateAck(envelope.Id);
            }

            case BlueType.Protocol.Commands.KeyDown:
            {
                var key = CommandPayloadReader.GetRequiredString(envelope.Payload, "key");
                await _inputService.PressKeyAsync(key, cancellationToken);
                AppLogger.Info($"Handled command: key_down ({key}).");
                return CreateAck(envelope.Id);
            }

            case BlueType.Protocol.Commands.KeyUp:
            {
                var key = CommandPayloadReader.GetRequiredString(envelope.Payload, "key");
                await _inputService.ReleaseKeyAsync(key, cancellationToken);
                AppLogger.Info($"Handled command: key_up ({key}).");
                return CreateAck(envelope.Id);
            }

            case BlueType.Protocol.Commands.Combo:
            {
                var keys = CommandPayloadReader.GetRequiredStringArray(envelope.Payload, "keys");
                await _inputService.SendComboAsync(keys, cancellationToken);
                AppLogger.Info($"Handled command: combo ({string.Join("+", keys)}).");
                return CreateAck(envelope.Id);
            }

            default:
                throw new InvalidOperationException($"Unsupported keyboard command: {envelope.Type}");
        }
    }

    private static Envelope CreateAck(string requestId)
    {
        return JsonProtocol.CreateEnvelope(requestId, Responses.Ack, new { ok = true });
    }
}
