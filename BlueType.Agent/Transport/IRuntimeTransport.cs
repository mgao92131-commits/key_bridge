using BlueType.Agent.Models;

namespace BlueType.Agent.Transport;

internal interface IRuntimeTransport : IDisposable
{
    event Action<ConnectionState>? ConnectionStateChanged;

    event Action<string>? ServerMessage;

    void Start();

    Task StopAsync(CancellationToken cancellationToken = default);

    bool DisconnectLatestClient();
}
