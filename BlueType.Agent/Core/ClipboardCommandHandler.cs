using System.Text;
using BlueType.Protocol;

namespace BlueType.Agent.Core;

internal sealed class ClipboardCommandHandler : ICommandHandler
{
    private readonly ClipboardService _clipboardService;

    public ClipboardCommandHandler(ClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public async Task<Envelope?> TryHandleAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        switch (envelope.Type)
        {
            case Commands.ClipboardSet:
            {
                var text = CommandPayloadReader.GetRequiredString(envelope.Payload, "text");
                await _clipboardService.SetTextAsync(text, cancellationToken);
                AppLogger.Info($"Handled command: clipboard_set ({Encoding.UTF8.GetByteCount(text)} bytes).");
                return JsonProtocol.CreateEnvelope(envelope.Id, Responses.Ack, new { ok = true });
            }

            case Commands.ClipboardGet:
            {
                var text = await _clipboardService.GetTextAsync(cancellationToken);
                AppLogger.Info("Handled command: clipboard_get.");
                return JsonProtocol.CreateEnvelope(envelope.Id, Responses.ClipboardValue, new { text });
            }

            default:
                return null;
        }
    }
}
