using BlueType.Agent.Core;
using BlueType.Agent.Tests.TestHelpers;
using BlueType.Agent.Transport;
using BlueType.Protocol;

namespace BlueType.Agent.Tests;

public sealed class SessionHeartbeatTests
{
    [Fact]
    public async Task TryHandleInboundAsync_WritesPong_WhenPingReceived()
    {
        var heartbeat = new SessionHeartbeat();
        await using var stream = new MemoryStream();
        await using var session = new ClientSession(stream);
        var ping = JsonProtocol.CreateEnvelope("ping-1", Commands.Ping, new { });

        var handled = await heartbeat.TryHandleInboundAsync(session, ping, CancellationToken.None);
        var responses = await EnvelopeTestReader.ReadAllAsync(stream);

        Assert.True(handled);
        Assert.Single(responses);
        Assert.Equal(Commands.Pong, responses[0].Type);
        Assert.Equal("ping-1", responses[0].Id);
    }
}
