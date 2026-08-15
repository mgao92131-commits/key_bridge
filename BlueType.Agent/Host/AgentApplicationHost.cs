using BlueType.Agent.Bootstrap;
using BlueType.Agent.Models;

namespace BlueType.Agent.Host;

internal sealed class AgentApplicationHost : IDisposable
{
    private readonly AgentRuntime _runtime;

    public AgentApplicationHost(Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> promptForAuthorizationAsync)
        : this(AgentCompositionRoot.Create(promptForAuthorizationAsync))
    {
    }

    internal AgentApplicationHost(AgentRuntime runtime)
    {
        _runtime = runtime;
        _runtime.ConnectionStateChanged += ForwardConnectionStateChanged;
        _runtime.ServerMessage += ForwardServerMessage;
    }

    public event Action<ConnectionState>? ConnectionStateChanged;

    public event Action<string>? ServerMessage;

    public void Start()
    {
        _runtime.Start();
    }

    public bool DisconnectActiveClient()
    {
        return _runtime.DisconnectActiveClient();
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _runtime.StopAsync(cancellationToken);
    }

    public void Dispose()
    {
        _runtime.ConnectionStateChanged -= ForwardConnectionStateChanged;
        _runtime.ServerMessage -= ForwardServerMessage;
        _runtime.Dispose();
    }

    private void ForwardConnectionStateChanged(ConnectionState state)
    {
        ConnectionStateChanged?.Invoke(state);
    }

    private void ForwardServerMessage(string message)
    {
        ServerMessage?.Invoke(message);
    }
}
