using System.Text;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Protocol;
using ProtocolCommands = BlueType.Protocol.Commands;

namespace BlueType.Agent.Application.Commands;

internal sealed class ClipboardCommandHandler : ICommandHandler
{
    private static readonly string[] CommandTypes =
    [
        ProtocolCommands.ClipboardSet,
        ProtocolCommands.ClipboardGet,
    ];

    private readonly ClipboardService _clipboardService;

    public ClipboardCommandHandler(ClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public IReadOnlyCollection<string> SupportedCommands => CommandTypes;

    public async Task<Envelope> HandleAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        switch (envelope.Type)
        {
            case BlueType.Protocol.Commands.ClipboardSet:
            {
                var text = CommandPayloadReader.GetRequiredString(envelope.Payload, "text");
                await _clipboardService.SetTextAsync(text, cancellationToken);
                AppLogger.Info($"Handled command: clipboard_set ({Encoding.UTF8.GetByteCount(text)} bytes).");
                return JsonProtocol.CreateEnvelope(envelope.Id, Responses.Ack, new { ok = true });
            }

            case BlueType.Protocol.Commands.ClipboardGet:
            {
                var text = await _clipboardService.GetTextAsync(cancellationToken);
                AppLogger.Info("Handled command: clipboard_get.");
                return JsonProtocol.CreateEnvelope(envelope.Id, Responses.ClipboardValue, new { text });
            }

            default:
                throw new InvalidOperationException($"Unsupported clipboard command: {envelope.Type}");
        }
    }
}
