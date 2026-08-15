using System.Diagnostics;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Agent.Models;
using BlueType.Agent.Application.Sessions;

namespace BlueType.Agent.Transport;

internal abstract class ConnectionServerBase<TClient> : IDisposable
    where TClient : class
{
    private readonly SessionProcessor _sessionProcessor;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _clientsLock = new();
    private readonly Dictionary<Guid, ActiveClientContext> _activeClients = [];
    private readonly object _sessionTasksLock = new();
    private readonly HashSet<Task> _sessionTasks = [];
    private readonly object _stopLock = new();

    private Task? _listenTask;
    private Task? _stopTask;
    private Guid? _latestSessionId;
    private int _stopRequested;
    private int _disposed;

    protected ConnectionServerBase(SessionProcessor sessionProcessor)
    {
        _sessionProcessor = sessionProcessor;
    }

    public event Action<ConnectionState>? ConnectionStateChanged;

    public event Action<string>? ServerMessage;

    protected abstract string TransportDisplayName { get; }

    protected abstract string SessionTransportName { get; }

    /// <summary>
    /// How long StopAsync waits for the accept loop after StopListening().
    /// Bluetooth blocking accept may ignore cancellation; keep this bounded.
    /// </summary>
    protected virtual TimeSpan ListenLoopShutdownTimeout => TimeSpan.FromSeconds(2);

    protected virtual TimeSpan SessionShutdownTimeout => TimeSpan.FromSeconds(5);

    protected CancellationToken ShutdownToken => _shutdown.Token;

    internal int TrackedSessionCount
    {
        get
        {
            lock (_sessionTasksLock)
            {
                return _sessionTasks.Count;
            }
        }
    }

    internal int ActiveClientCount
    {
        get
        {
            lock (_clientsLock)
            {
                return _activeClients.Count;
            }
        }
    }

    protected void StartAcceptLoop(string serverMessage, string logMessage)
    {
        EmitConnectionState(ConnectionState.Listening);
        EmitServerMessage(serverMessage);
        AppLogger.Info(logMessage);
        _listenTask = Task.Run(RunAcceptLoopAsync);
    }

    protected void EmitConnectionState(ConnectionState state)
    {
        ConnectionStateChanged?.Invoke(state);
    }

    protected void EmitServerMessage(string message)
    {
        ServerMessage?.Invoke(message);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_stopLock)
        {
            if (_stopTask is not null)
            {
                return _stopTask;
            }

            _stopTask = StopCoreAsync(cancellationToken);
            return _stopTask;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            // Sync fallback only; tray exit must use StopAsync on a non-blocking path.
            if (!StopAsync().Wait(TimeSpan.FromSeconds(3)))
            {
                AppLogger.Warn($"{TransportDisplayName} Dispose timed out waiting for StopAsync.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"{TransportDisplayName} Dispose stop failed.", ex);
        }
        finally
        {
            try
            {
                _shutdown.Dispose();
            }
            catch
            {
                // Best effort only.
            }
        }
    }

    /// <summary>
    /// Disconnects the latest transport-level connection on this server.
    /// Used as a fallback when no protocol-controlling session exists yet
    /// (for example during PendingApproval). Prefer Host-level disconnect of the
    /// ActiveSessionManager controlling session for "Disconnect Current Client".
    /// </summary>
    public bool DisconnectLatestClient()
    {
        ActiveClientContext? target;

        lock (_clientsLock)
        {
            if (_latestSessionId is Guid latestId &&
                _activeClients.TryGetValue(latestId, out var latest))
            {
                target = latest;
            }
            else if (_activeClients.Count > 0)
            {
                target = _activeClients.Values.Last();
            }
            else
            {
                target = null;
            }
        }

        if (target is null)
        {
            return false;
        }

        CancelAndCloseClient(target.Client, target.Lifetime);
        AppLogger.Info(
            $"Manual disconnect requested for {TransportDisplayName} client {target.RemoteAddress ?? "unknown"} (session {target.SessionId}).");
        return true;
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
        {
            return;
        }

        var startedAt = Stopwatch.StartNew();
        AppLogger.Info($"Stopping {TransportDisplayName} server.");

        var closedCount = DisconnectAllClients();
        AppLogger.Info($"{TransportDisplayName} closed {closedCount} active client(s) during shutdown.");

        try
        {
            _shutdown.Cancel();
        }
        catch
        {
            // Best effort only.
        }

        try
        {
            StopListening();
            AppLogger.Info($"{TransportDisplayName} listener stopped.");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"{TransportDisplayName} listener stop failed.", ex);
        }

        if (_listenTask is not null)
        {
            try
            {
                await _listenTask.WaitAsync(ListenLoopShutdownTimeout, cancellationToken);
                AppLogger.Info($"{TransportDisplayName} accept loop completed.");
            }
            catch (TimeoutException)
            {
                AppLogger.Warn(
                    $"{TransportDisplayName} accept loop did not stop within {ListenLoopShutdownTimeout.TotalMilliseconds:0} ms.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"{TransportDisplayName} accept loop wait failed.", ex);
            }
        }

        Task[] sessions;
        lock (_sessionTasksLock)
        {
            sessions = _sessionTasks.ToArray();
        }

        AppLogger.Info($"{TransportDisplayName} sessions remaining: {sessions.Length}.");
        if (sessions.Length > 0)
        {
            try
            {
                await Task.WhenAll(sessions).WaitAsync(SessionShutdownTimeout, cancellationToken);
                AppLogger.Info($"{TransportDisplayName} sessions completed.");
            }
            catch (TimeoutException)
            {
                AppLogger.Warn(
                    $"{TransportDisplayName} sessions did not complete within {SessionShutdownTimeout.TotalMilliseconds:0} ms.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"{TransportDisplayName} session wait failed.", ex);
            }
        }

        AppLogger.Info($"{TransportDisplayName} server stopped in {startedAt.ElapsedMilliseconds} ms.");
    }

    private int DisconnectAllClients()
    {
        ActiveClientContext[] clients;
        lock (_clientsLock)
        {
            clients = _activeClients.Values.ToArray();
        }

        foreach (var context in clients)
        {
            CancelAndCloseClient(context.Client, context.Lifetime);
        }

        return clients.Length;
    }

    private async Task RunAcceptLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            TClient? client = null;
            try
            {
                client = await AcceptClientAsync(_shutdown.Token);
                TrackSession(HandleClientAsync(client, _shutdown.Token));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (InvalidOperationException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (client is not null)
                {
                    try
                    {
                        DisposeClient(client);
                    }
                    catch
                    {
                        // Best effort only.
                    }
                }

                if (_shutdown.IsCancellationRequested)
                {
                    break;
                }

                EmitServerMessage($"{TransportDisplayName} accept failed: {ex.Message}");
                AppLogger.Error($"{TransportDisplayName} accept failed.", ex);
            }
        }
    }

    private void TrackSession(Task sessionTask)
    {
        lock (_sessionTasksLock)
        {
            _sessionTasks.Add(sessionTask);
        }

        _ = sessionTask.ContinueWith(
            completed =>
            {
                lock (_sessionTasksLock)
                {
                    _sessionTasks.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task HandleClientAsync(TClient client, CancellationToken cancellationToken)
    {
        var remoteAddress = GetRemoteAddress(client);
        var sessionId = Guid.NewGuid();
        using var sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        RegisterActiveClient(sessionId, client, sessionLifetime, remoteAddress);
        void DisconnectCurrentSession()
        {
            CancelAndCloseClient(client, sessionLifetime);
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
            ClearActiveClient(sessionId);
            try
            {
                DisposeClient(client);
            }
            catch
            {
                // Best effort only.
            }

            EmitConnectionState(ConnectionState.Listening);
            AppLogger.Info($"{TransportDisplayName} client disconnected from {remoteAddress ?? "unknown"}.");
        }
    }

    private void RegisterActiveClient(
        Guid sessionId,
        TClient client,
        CancellationTokenSource lifetime,
        string? remoteAddress)
    {
        lock (_clientsLock)
        {
            _activeClients[sessionId] = new ActiveClientContext(sessionId, client, lifetime, remoteAddress);
            _latestSessionId = sessionId;
        }
    }

    private void ClearActiveClient(Guid sessionId)
    {
        lock (_clientsLock)
        {
            _activeClients.Remove(sessionId);
            if (_latestSessionId == sessionId)
            {
                _latestSessionId = _activeClients.Count == 0
                    ? null
                    : _activeClients.Keys.Last();
            }
        }
    }

    private void CancelAndCloseClient(TClient? client, CancellationTokenSource? lifetime)
    {
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
    }

    protected abstract Task<TClient> AcceptClientAsync(CancellationToken cancellationToken);

    protected abstract ClientSession CreateSession(TClient client);

    protected abstract void CloseClient(TClient? client);

    protected abstract void DisposeClient(TClient client);

    protected abstract string? GetRemoteAddress(TClient client);

    protected abstract void StopListening();

    private sealed record ActiveClientContext(
        Guid SessionId,
        TClient Client,
        CancellationTokenSource Lifetime,
        string? RemoteAddress);
}
