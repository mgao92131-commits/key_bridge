using BlueType.Agent.Transport;
using BlueType.Agent.Infrastructure.Logging;
using BlueType.Protocol;

namespace BlueType.Agent.Core;

internal sealed class SessionHeartbeat
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(90);

    public async Task<bool> TryHandleInboundAsync(ClientSession session, Envelope envelope, CancellationToken cancellationToken)
    {
        if (string.Equals(envelope.Type, Commands.Ping, StringComparison.Ordinal))
        {
            await session.WriteAsync(CreatePongEnvelope(envelope.Id), cancellationToken);
            return true;
        }

        return string.Equals(envelope.Type, Commands.Pong, StringComparison.Ordinal);
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
                await Task.Delay(HeartbeatInterval, sessionLifetime.Token);

                var silence = TimeSpan.FromMilliseconds(Environment.TickCount64 - getLastInboundAt());
                if (silence >= HeartbeatTimeout)
                {
                    var endpoint = remoteAddress ?? "unknown";
                    var message = $"{transport} client {endpoint} timed out after {HeartbeatTimeout.TotalSeconds:0} seconds.";
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
        return JsonProtocol.CreateEnvelope(Guid.NewGuid().ToString("D"), Commands.Ping, new { });
    }

    private static Envelope CreatePongEnvelope(string requestId)
    {
        return JsonProtocol.CreateEnvelope(requestId, Commands.Pong, new { });
    }
}
