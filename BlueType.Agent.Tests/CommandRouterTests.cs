using BlueType.Agent.Core;
using BlueType.Agent.Application.Commands;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Tests.TestHelpers;
using BlueType.Protocol;

namespace BlueType.Agent.Tests;

public sealed class CommandRouterTests
{
    [Fact]
    public async Task RouteAsync_ReturnsPong_WhenPingReceived()
    {
        using var harness = new CommandHarness();
        var envelope = JsonProtocol.CreateEnvelope("ping-2", Commands.Ping, new { });

        var response = await harness.Router.RouteAsync(envelope, CancellationToken.None);

        Assert.Equal(Commands.Pong, response.Type);
        Assert.True(EnvelopeTestReader.GetBoolean(response, "ok"));
    }

    [Fact]
    public async Task RouteAsync_ReturnsInvalidPayloadError_WhenCommandIsUnknown()
    {
        using var harness = new CommandHarness();
        var envelope = JsonProtocol.CreateEnvelope("unknown-1", "unknown_type", new { });

        var response = await harness.Router.RouteAsync(envelope, CancellationToken.None);

        Assert.Equal(Responses.Error, response.Type);
        Assert.Equal("INVALID_PAYLOAD", EnvelopeTestReader.GetString(response, "code"));
    }

    [Fact]
    public async Task RouteAsync_ReturnsInvalidPayloadError_WhenTextInsertExceedsLimit()
    {
        using var harness = new CommandHarness();
        var oversizedText = new string('A', 9000);
        var envelope = JsonProtocol.CreateEnvelope("text-oversized", Commands.TextInsert, new { text = oversizedText });

        var response = await harness.Router.RouteAsync(envelope, CancellationToken.None);

        Assert.Equal(Responses.Error, response.Type);
        Assert.Equal("INVALID_PAYLOAD", EnvelopeTestReader.GetString(response, "code"));
        Assert.Equal("Text payload exceeds 8 KB.", EnvelopeTestReader.GetString(response, "message"));
    }

    private sealed class CommandHarness : IDisposable
    {
        private readonly InputInjector _inputInjector = new();
        private readonly ClipboardService _clipboardService = new();

        public CommandRouter Router { get; }

        public CommandHarness()
        {
            Router = new CommandRouter(_inputInjector, _clipboardService);
        }

        public void Dispose()
        {
            _clipboardService.Dispose();
            _inputInjector.Dispose();
        }
    }
}
