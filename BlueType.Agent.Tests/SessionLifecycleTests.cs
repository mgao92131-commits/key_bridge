using BlueType.Agent.Application.Commands;
using BlueType.Agent.Application.Sessions;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Tests.TestHelpers;
using BlueType.Protocol;

namespace BlueType.Agent.Tests;

public sealed class SessionLifecycleTests
{
    [Fact]
    public void ValidateCommandEnvelope_ReturnsNotAuthorizedBeforeHelloCompletes()
    {
        using var harness = new CommandHarness();
        var lifecycle = new SessionLifecycle(harness.Dispatcher, new ActiveSessionManager(), Guid.NewGuid());
        var envelope = JsonProtocol.CreateEnvelope("key-1", Commands.KeyTap, new { key = "ENTER" });

        var error = lifecycle.ValidateCommandEnvelope(envelope);

        Assert.NotNull(error);
        Assert.Equal(Responses.Error, error.Type);
        Assert.Equal("NOT_AUTHORIZED", EnvelopeTestReader.GetString(error, "code"));
    }

    [Fact]
    public void ValidateCommandEnvelope_ReturnsSessionReplacedWhenSessionIsNoLongerActive()
    {
        using var harness = new CommandHarness();
        var lifecycle = new SessionLifecycle(harness.Dispatcher, new ActiveSessionManager(), Guid.NewGuid());
        lifecycle.MarkAuthorized();
        var envelope = JsonProtocol.CreateEnvelope("key-2", Commands.KeyTap, new { key = "ENTER" });

        var error = lifecycle.ValidateCommandEnvelope(envelope);

        Assert.NotNull(error);
        Assert.Equal(Responses.Error, error.Type);
        Assert.Equal("SESSION_REPLACED", EnvelopeTestReader.GetString(error, "code"));
    }

    [Fact]
    public void CreateDuplicateHelloError_ReturnsInvalidPayload()
    {
        using var harness = new CommandHarness();
        var lifecycle = new SessionLifecycle(harness.Dispatcher, new ActiveSessionManager(), Guid.NewGuid());

        var error = lifecycle.CreateDuplicateHelloError("hello-2");

        Assert.Equal("hello-2", error.Id);
        Assert.Equal(Responses.Error, error.Type);
        Assert.Equal("INVALID_PAYLOAD", EnvelopeTestReader.GetString(error, "code"));
        Assert.Equal("HELLO already completed.", EnvelopeTestReader.GetString(error, "message"));
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
}
