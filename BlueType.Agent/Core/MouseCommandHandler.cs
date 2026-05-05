using BlueType.Protocol;

namespace BlueType.Agent.Core;

internal sealed class MouseCommandHandler : ICommandHandler
{
    private readonly InputInjector _inputInjector;

    public MouseCommandHandler(InputInjector inputInjector)
    {
        _inputInjector = inputInjector;
    }

    public async Task<Envelope?> TryHandleAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        switch (envelope.Type)
        {
            case Commands.MouseMove:
            {
                var dx = CommandPayloadReader.GetRequiredInt(envelope.Payload, "dx");
                var dy = CommandPayloadReader.GetRequiredInt(envelope.Payload, "dy");
                await _inputInjector.MoveMouseAsync(dx, dy, cancellationToken);
                AppLogger.Info($"Handled command: mouse_move ({dx},{dy}).");
                return CreateAck(envelope.Id);
            }

            case Commands.MouseButton:
            {
                var button = CommandPayloadReader.GetRequiredString(envelope.Payload, "button");
                var action = CommandPayloadReader.GetRequiredString(envelope.Payload, "action");
                switch (action.Trim().ToLowerInvariant())
                {
                    case "down":
                        await _inputInjector.PressMouseAsync(button, cancellationToken);
                        break;
                    case "up":
                        await _inputInjector.ReleaseMouseAsync(button, cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported mouse button action: {action}");
                }

                AppLogger.Info($"Handled command: mouse_button ({button} {action}).");
                return CreateAck(envelope.Id);
            }

            case Commands.MouseClick:
            {
                var button = CommandPayloadReader.GetRequiredString(envelope.Payload, "button");
                var repeat = CommandPayloadReader.GetOptionalInt(envelope.Payload, "repeat", 1);
                await _inputInjector.ClickMouseAsync(button, repeat, cancellationToken);
                AppLogger.Info($"Handled command: mouse_click ({button} x{repeat}).");
                return CreateAck(envelope.Id);
            }

            case Commands.MouseScroll:
            {
                var deltaX = CommandPayloadReader.GetOptionalInt(envelope.Payload, "deltaX", 0);
                var deltaY = CommandPayloadReader.GetOptionalInt(envelope.Payload, "deltaY", 0);
                await _inputInjector.ScrollMouseAsync(deltaX, deltaY, cancellationToken);
                AppLogger.Info($"Handled command: mouse_scroll ({deltaX},{deltaY}).");
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
