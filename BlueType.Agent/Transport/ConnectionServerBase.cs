using BlueType.Agent.Core;
using BlueType.Agent.Models;

namespace BlueType.Agent.Transport;

internal abstract class ConnectionServerBase<TClient> : IDisposable
    where TClient : class
{
    private readonly SessionProcessor _sessionProcessor;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _activeClientLock = new();
    private Task? _listenTask;
    private TClient? _activeClient;
    private CancellationTokenSource? _activeClientLifetime;
    private string? _activeRemoteAddress;

    protected ConnectionServerBase(SessionProcessor sessionProcessor)
    {
        _sessionProcessor = sessionProcessor;
    }

    public event Action<ConnectionState>? ConnectionStateChanged;

    public event Action<string>? ServerMessage;

    protected abstract string TransportDisplayName { get; }

    protected abstract string SessionTransportName { get; }

    protected void StartAcceptLoop(string serverMessage, string logMessage)
    {
        EmitConnectionState(ConnectionState.Listening);
        EmitServerMessage(serverMessage);
        AppLogger.Info(logMessage);
        _listenTask = Task.Run(ListenLoopAsync);
    }

    protected void EmitConnectionState(ConnectionState state)
    {
        ConnectionStateChanged?.Invoke(state);
    }

    protected void EmitServerMessage(string message)
    {
        ServerMessage?.Invoke(message);
    }

    public void Dispose()
    {
        DisconnectActiveClient();
        _shutdown.Cancel();
        StopListening();
        try
        {
            _listenTask?.GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore shutdown exceptions during app exit.
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    public bool DisconnectActiveClient()
    {
        TClient? client;
        CancellationTokenSource? lifetime;
        string? remoteAddress;

        lock (_activeClientLock)
        {
            client = _activeClient;
            lifetime = _activeClientLifetime;
            remoteAddress = _activeRemoteAddress;
        }

        if (client is null && lifetime is null)
        {
            return false;
        }

        try
        {
            lifetime?.Cancel();
        }
        catch
        {
            // Best effort only.
        }

        try
        {
            CloseClient(client);
        }
        catch
        {
            // Best effort only.
        }

        AppLogger.Info($"Manual disconnect requested for {TransportDisplayName} client {remoteAddress ?? "unknown"}.");
        return true;
    }

    private async Task ListenLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            TClient? client = null;
            try
            {
                client = await AcceptClientAsync(_shutdown.Token);
                _ = HandleClientAsync(client, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (client is not null)
                {
                    DisposeClient(client);
                }

                EmitServerMessage($"{TransportDisplayName} accept failed: {ex.Message}");
                AppLogger.Error($"{TransportDisplayName} accept failed.", ex);
            }
        }
    }

    private async Task HandleClientAsync(TClient client, CancellationToken cancellationToken)
    {
        var remoteAddress = GetRemoteAddress(client);
        var sessionId = Guid.NewGuid();
        using var sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        RegisterActiveClient(client, sessionLifetime, remoteAddress);
        void DisconnectCurrentSession()
        {
            try
            {
                sessionLifetime.Cancel();
            }
            catch
            {
                // Best effort only.
            }

            try
            {
                CloseClient(client);
            }
            catch
            {
                // Best effort only.
            }
        }

        try
        {
            await using var session = CreateSession(client);
            await _sessionProcessor.RunAsync(
                session,
                remoteAddress,
                SessionTransportName,
                ConnectionStateChanged,
                ServerMessage,
                sessionId,
                DisconnectCurrentSession,
                sessionLifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            EmitServerMessage($"{TransportDisplayName} session failed: {ex.Message}");
            AppLogger.Error($"{TransportDisplayName} session failed.", ex);
        }
        finally
        {
            ClearActiveClient(client);
            DisposeClient(client);
            EmitConnectionState(ConnectionState.Listening);
            AppLogger.Info($"{TransportDisplayName} client disconnected from {remoteAddress ?? "unknown"}.");
        }
    }

    private void RegisterActiveClient(TClient client, CancellationTokenSource lifetime, string? remoteAddress)
    {
        lock (_activeClientLock)
        {
            _activeClient = client;
            _activeClientLifetime = lifetime;
            _activeRemoteAddress = remoteAddress;
        }
    }

    private void ClearActiveClient(TClient client)
    {
        CancellationTokenSource? lifetimeToDispose = null;

        lock (_activeClientLock)
        {
            if (!ReferenceEquals(_activeClient, client))
            {
                return;
            }

            lifetimeToDispose = _activeClientLifetime;
            _activeClient = null;
            _activeClientLifetime = null;
            _activeRemoteAddress = null;
        }

        lifetimeToDispose?.Dispose();
    }

    protected abstract Task<TClient> AcceptClientAsync(CancellationToken cancellationToken);

    protected abstract ClientSession CreateSession(TClient client);

    protected abstract void CloseClient(TClient? client);

    protected abstract void DisposeClient(TClient client);

    protected abstract string? GetRemoteAddress(TClient client);

    protected abstract void StopListening();
}
