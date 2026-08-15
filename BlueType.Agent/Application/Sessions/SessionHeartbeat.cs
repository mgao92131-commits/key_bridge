using BlueType.Agent.Transport;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Protocol;

namespace BlueType.Agent.Application.Sessions;

internal sealed class SessionHeartbeat
{
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultHeartbeatTimeout = TimeSpan.FromSeconds(90);
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _heartbeatTimeout;

    public SessionHeartbeat()
        : this(DefaultHeartbeatInterval, DefaultHeartbeatTimeout)
    {
    }

    internal SessionHeartbeat(TimeSpan heartbeatInterval, TimeSpan heartbeatTimeout)
    {
        _heartbeatInterval = heartbeatInterval;
        _heartbeatTimeout = heartbeatTimeout;
    }

    public async Task<bool> TryHandleInboundAsync(ClientSession session, Envelope envelope, CancellationToken cancellationToken)
    {
        if (string.Equals(envelope.Type, BlueType.Protocol.Commands.Ping, StringComparison.Ordinal))
        {
            await session.WriteAsync(CreatePongEnvelope(envelope.Id), cancellationToken);
            return true;
        }

        return string.Equals(envelope.Type, BlueType.Protocol.Responses.Pong, StringComparison.Ordinal);
    }

    public async Task RunAsync(
        ClientSession session,
        string transport,
        string? remoteAddress,
        Func<long> getLastInboundAt,
        Action<string>? onMessage,
        CancellationTokenSource sessionLifetime)
    {
        try
        {
            while (!sessionLifetime.IsCancellationRequested)
            {
                await Task.Delay(_heartbeatInterval, sessionLifetime.Token);

                var silence = TimeSpan.FromMilliseconds(Environment.TickCount64 - getLastInboundAt());
                if (silence >= _heartbeatTimeout)
                {
                    var endpoint = remoteAddress ?? "unknown";
                    var message = $"{transport} client {endpoint} timed out after {_heartbeatTimeout.TotalSeconds:0} seconds.";
                    onMessage?.Invoke(message);
                    AppLogger.Info(message);

                    await session.DisposeAsync();
                    sessionLifetime.Cancel();
                    break;
                }

                await session.WriteAsync(CreatePingEnvelope(), sessionLifetime.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Heartbeat failed for {transport} client {remoteAddress ?? "unknown"}.", ex);
            await session.DisposeAsync();
            sessionLifetime.Cancel();
        }
    }

    private static Envelope CreatePingEnvelope()
    {
        return JsonProtocol.CreateEnvelope(Guid.NewGuid().ToString("D"), BlueType.Protocol.Commands.Ping, new { });
    }

    private static Envelope CreatePongEnvelope(string requestId)
    {
        return JsonProtocol.CreateEnvelope(requestId, BlueType.Protocol.Responses.Pong, new { });
    }
}
