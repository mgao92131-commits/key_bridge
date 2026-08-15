namespace BlueType.Agent.Application.Ports;

internal interface IForegroundAppProvider
{
    string? GetCurrentAppId();
}
