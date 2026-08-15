using BlueType.Agent.Application.Commands;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Tests.TestHelpers;
using BlueType.Protocol;

namespace BlueType.Agent.Tests;

public sealed class CommandDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_ReturnsPong_WhenPingReceived()
    {
        using var harness = new CommandHarness();
        var envelope = JsonProtocol.CreateEnvelope("ping-2", Commands.Ping, new { });

        var response = await harness.Dispatcher.DispatchAsync(envelope, CancellationToken.None);

        Assert.Equal(Commands.Pong, response.Type);
        Assert.True(EnvelopeTestReader.GetBoolean(response, "ok"));
    }

    [Fact]
    public async Task DispatchAsync_ReturnsAck_WhenKeyboardCommandIsReceived()
    {
        using var harness = new CommandHarness();
        var envelope = JsonProtocol.CreateEnvelope(
            "text-1",
            Commands.TextInsert,
            new { text = string.Empty });

        var response = await harness.Dispatcher.DispatchAsync(envelope, CancellationToken.None);

        Assert.Equal(Responses.Ack, response.Type);
        Assert.Equal(envelope.Id, response.Id);
    }

    [Fact]
    public async Task DispatchAsync_ReturnsAck_WhenMouseCommandIsReceived()
    {
        using var harness = new CommandHarness();
        var envelope = JsonProtocol.CreateEnvelope(
            "move-1",
            Commands.MouseMove,
            new { dx = 0, dy = 0 });

        var response = await harness.Dispatcher.DispatchAsync(envelope, CancellationToken.None);

        Assert.Equal(Responses.Ack, response.Type);
        Assert.Equal(envelope.Id, response.Id);
    }

    [Fact]
    public async Task DispatchAsync_ReturnsAck_WhenClipboardCommandIsReceived()
    {
        using var harness = new CommandHarness();
        var envelope = JsonProtocol.CreateEnvelope(
            "clipboard-1",
            Commands.ClipboardSet,
            new { text = "   " });

        var response = await harness.Dispatcher.DispatchAsync(envelope, CancellationToken.None);

        Assert.Equal(Responses.Ack, response.Type);
        Assert.Equal(envelope.Id, response.Id);
    }

    [Fact]
    public async Task DispatchAsync_ReturnsInvalidPayloadError_WhenCommandIsUnknown()
    {
        using var harness = new CommandHarness();
        var envelope = JsonProtocol.CreateEnvelope("unknown-1", "unknown_type", new { });

        var response = await harness.Dispatcher.DispatchAsync(envelope, CancellationToken.None);

        Assert.Equal(Responses.Error, response.Type);
        Assert.Equal("INVALID_PAYLOAD", EnvelopeTestReader.GetString(response, "code"));
    }

    [Fact]
    public async Task DispatchAsync_ReturnsInvalidPayloadError_WhenTextInsertExceedsLimit()
    {
        using var harness = new CommandHarness();
        var oversizedText = new string('A', 9000);
        var envelope = JsonProtocol.CreateEnvelope("text-oversized", Commands.TextInsert, new { text = oversizedText });

        var response = await harness.Dispatcher.DispatchAsync(envelope, CancellationToken.None);

        Assert.Equal(Responses.Error, response.Type);
        Assert.Equal("INVALID_PAYLOAD", EnvelopeTestReader.GetString(response, "code"));
        Assert.Equal("Text payload exceeds 8 KB.", EnvelopeTestReader.GetString(response, "message"));
    }

    [Fact]
    public async Task DispatchAsync_PropagatesHandlerException_WhenHandlerFails()
    {
        using var harness = new CommandHarness();
        var envelope = JsonProtocol.CreateEnvelope(
            "mouse-invalid",
            Commands.MouseButton,
            new { button = "LEFT", action = "sideways" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Dispatcher.DispatchAsync(envelope, CancellationToken.None));

        Assert.Contains("Unsupported mouse button action", exception.Message);
    }

    [Fact]
    public async Task DispatchAsync_PropagatesCancellationToken_ToHandler()
    {
        using var harness = new CommandHarness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var envelope = JsonProtocol.CreateEnvelope(
            "text-cancelled",
            Commands.TextInsert,
            new { text = "A" });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Dispatcher.DispatchAsync(envelope, cancellation.Token));
    }

    [Fact]
    public void Constructor_Throws_WhenCommandRegisteredByMultipleHandlers()
    {
        var handlers = new ICommandHandler[]
        {
            new StubCommandHandler(Commands.KeyDown),
            new StubCommandHandler(Commands.KeyDown),
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new CommandDispatcher(handlers));

        Assert.Contains(Commands.KeyDown, exception.Message);
        Assert.Contains("multiple handlers", exception.Message);
    }

    private sealed class CommandHarness : IDisposable
    {
        private readonly InputInjector _inputInjector = new();
        private readonly ClipboardService _clipboardService = new();

        public CommandDispatcher Dispatcher { get; }

        public CommandHarness()
        {
            Dispatcher = CommandDispatcherTestFactory.Create(_inputInjector, _clipboardService);
        }

        public void Dispose()
        {
            _clipboardService.Dispose();
            _inputInjector.Dispose();
        }
    }

    private sealed class StubCommandHandler : ICommandHandler
    {
        public StubCommandHandler(string commandType)
        {
            SupportedCommands = [commandType];
        }

        public IReadOnlyCollection<string> SupportedCommands { get; }

        public Task<Envelope> HandleAsync(Envelope envelope, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                JsonProtocol.CreateEnvelope(envelope.Id, Responses.Ack, new { ok = true }));
        }
    }
}
