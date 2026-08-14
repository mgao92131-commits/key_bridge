using System.Diagnostics;
using BlueType.Agent.Core;
using BlueType.Agent.Models;

namespace BlueType.Agent.Transport;

internal abstract class ConnectionServerBase<TClient> : IDisposable
    where TClient : class
{
    private readonly SessionProcessor _sessionProcessor;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _activeClientLock = new();
    private readonly object _sessionTasksLock = new();
    private readonly HashSet<Task> _sessionTasks = [];
    private readonly object _stopLock = new();

    private Task? _listenTask;
    private Task? _stopTask;
    private TClient? _activeClient;
    private CancellationTokenSource? _activeClientLifetime;
    private string? _activeRemoteAddress;
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

        CancelAndCloseClient(client, lifetime);
        AppLogger.Info($"Manual disconnect requested for {TransportDisplayName} client {remoteAddress ?? "unknown"}.");
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

        DisconnectActiveClient();

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
        RegisterActiveClient(client, sessionLifetime, remoteAddress);
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
            ClearActiveClient(client);
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
}
