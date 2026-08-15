namespace BlueType.Agent.Application.Ports;

internal interface IInputRelease
{
    Task ReleaseAllKeysAsync(CancellationToken cancellationToken = default);

    Task ReleaseAllMouseButtonsAsync(CancellationToken cancellationToken = default);
}
