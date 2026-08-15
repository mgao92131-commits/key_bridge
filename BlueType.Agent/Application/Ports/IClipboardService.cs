namespace BlueType.Agent.Application.Ports;

internal interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);

    Task<string> GetTextAsync(CancellationToken cancellationToken = default);
}
