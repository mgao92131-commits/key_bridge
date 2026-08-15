using BlueType.Protocol;
using BlueType.Agent.Application.Ports;
using BlueType.Agent.Infrastructure.Logging;
using ProtocolCommands = BlueType.Protocol.Commands;

namespace BlueType.Agent.Application.Commands;

internal sealed class MouseCommandHandler : ICommandHandler
{
    private static readonly string[] CommandTypes =
    [
        ProtocolCommands.MouseMove,
        ProtocolCommands.MouseButton,
        ProtocolCommands.MouseClick,
        ProtocolCommands.MouseScroll,
    ];

    private readonly IInputService _inputService;

    public MouseCommandHandler(IInputService inputService)
    {
        _inputService = inputService;
    }

    public IReadOnlyCollection<string> SupportedCommands => CommandTypes;

    public async Task<Envelope> HandleAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        switch (envelope.Type)
        {
            case BlueType.Protocol.Commands.MouseMove:
            {
                var dx = CommandPayloadReader.GetRequiredInt(envelope.Payload, "dx");
                var dy = CommandPayloadReader.GetRequiredInt(envelope.Payload, "dy");
                await _inputService.MoveMouseAsync(dx, dy, cancellationToken);
                AppLogger.Info($"Handled command: mouse_move ({dx},{dy}).");
                return CreateAck(envelope.Id);
            }

            case BlueType.Protocol.Commands.MouseButton:
            {
                var button = CommandPayloadReader.GetRequiredString(envelope.Payload, "button");
                var action = CommandPayloadReader.GetRequiredString(envelope.Payload, "action");
                switch (action.Trim().ToLowerInvariant())
                {
                    case "down":
                        await _inputService.PressMouseAsync(button, cancellationToken);
                        break;
                    case "up":
                        await _inputService.ReleaseMouseAsync(button, cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported mouse button action: {action}");
                }

                AppLogger.Info($"Handled command: mouse_button ({button} {action}).");
                return CreateAck(envelope.Id);
            }

            case BlueType.Protocol.Commands.MouseClick:
            {
                var button = CommandPayloadReader.GetRequiredString(envelope.Payload, "button");
                var repeat = CommandPayloadReader.GetOptionalInt(envelope.Payload, "repeat", 1);
                await _inputService.ClickMouseAsync(button, repeat, cancellationToken);
                AppLogger.Info($"Handled command: mouse_click ({button} x{repeat}).");
                return CreateAck(envelope.Id);
            }

            case BlueType.Protocol.Commands.MouseScroll:
            {
                var deltaX = CommandPayloadReader.GetOptionalInt(envelope.Payload, "deltaX", 0);
                var deltaY = CommandPayloadReader.GetOptionalInt(envelope.Payload, "deltaY", 0);
                await _inputService.ScrollMouseAsync(deltaX, deltaY, cancellationToken);
                AppLogger.Info($"Handled command: mouse_scroll ({deltaX},{deltaY}).");
                return CreateAck(envelope.Id);
            }

            default:
                throw new InvalidOperationException($"Unsupported mouse command: {envelope.Type}");
        }
    }

    private static Envelope CreateAck(string requestId)
    {
        return JsonProtocol.CreateEnvelope(requestId, Responses.Ack, new { ok = true });
    }
}
