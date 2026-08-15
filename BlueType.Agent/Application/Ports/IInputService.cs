namespace BlueType.Agent.Application.Ports;

internal interface IInputService
{
    Task SendTextAsync(string text, CancellationToken cancellationToken = default);

    Task TapKeyAsync(string key, CancellationToken cancellationToken = default);

    Task PressKeyAsync(string key, CancellationToken cancellationToken = default);

    Task ReleaseKeyAsync(string key, CancellationToken cancellationToken = default);

    Task SendComboAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);

    Task MoveMouseAsync(int dx, int dy, CancellationToken cancellationToken = default);

    Task ClickMouseAsync(string button, int repeat, CancellationToken cancellationToken = default);

    Task PressMouseAsync(string button, CancellationToken cancellationToken = default);

    Task ReleaseMouseAsync(string button, CancellationToken cancellationToken = default);

    Task ScrollMouseAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default);
}
