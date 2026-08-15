using BlueType.Agent.Core;
using BlueType.Agent.Application.Commands;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Models;
using BlueType.Agent.Tests.TestHelpers;
using BlueType.Agent.Transport;
using BlueType.Protocol;

namespace BlueType.Agent.Tests;

public sealed class SessionHelloHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsInvalidPayloadError_WhenHelloPayloadIsMalformed()
    {
        using var harness = new CommandHarness();
        var authService = new AuthService(new DeviceRegistry(), (_, _) => Task.FromResult(AuthPromptDecision.AllowOnce));
        var handler = new SessionHelloHandler(harness.Router, authService);
        await using var session = harness.CreateSession();

        var envelope = JsonProtocol.CreateEnvelope(
            "hello-1",
            Commands.Hello,
            new
            {
                deviceId = "device-123",
            });

        var result = await handler.HandleAsync(session, envelope, "127.0.0.1:24862", "wifi", null, null, CancellationToken.None);
        var responses = await EnvelopeTestReader.ReadAllAsync(harness.Stream);

        Assert.False(result);
        Assert.Single(responses);
        Assert.Equal(Responses.Error, responses[0].Type);
        Assert.Equal("INVALID_PAYLOAD", EnvelopeTestReader.GetString(responses[0], "code"));
    }

    [Fact]
    public async Task HandleAsync_WritesPendingAndAuthResult_WhenApprovalSucceeds()
    {
        using var harness = new CommandHarness();
        var states = new List<ConnectionState>();
        var messages = new List<string>();
        var authService = new AuthService(new DeviceRegistry(), (_, _) => Task.FromResult(AuthPromptDecision.AllowOnce));
        var handler = new SessionHelloHandler(harness.Router, authService);
        await using var session = harness.CreateSession();

        var envelope = JsonProtocol.CreateEnvelope(
            "hello-2",
            Commands.Hello,
            new
            {
                deviceId = "device-456",
                deviceName = "Pixel Remote",
                appVersion = "1.0.0",
            });

        var result = await handler.HandleAsync(
            session,
            envelope,
            "127.0.0.1:24862",
            "wifi",
            states.Add,
            messages.Add,
            CancellationToken.None);
        var responses = await EnvelopeTestReader.ReadAllAsync(harness.Stream);

        Assert.True(result);
        Assert.Collection(
            states,
            state => Assert.Equal(ConnectionState.Authenticating, state),
            state => Assert.Equal(ConnectionState.PendingApproval, state),
            state => Assert.Equal(ConnectionState.Connected, state));
        Assert.Single(messages);
        Assert.Contains("Authorized wifi device", messages[0]);
        Assert.Equal(2, responses.Count);
        Assert.Equal(Responses.AuthPending, responses[0].Type);
        Assert.Equal(Responses.AuthResult, responses[1].Type);
        Assert.True(EnvelopeTestReader.GetBoolean(responses[1], "ok"));
        Assert.False(EnvelopeTestReader.GetBoolean(responses[1], "persistToken"));
        Assert.False(EnvelopeTestReader.GetBoolean(responses[1], "trusted"));
    }

    private sealed class CommandHarness : IDisposable
    {
        private readonly InputInjector _inputInjector = new();
        private readonly ClipboardService _clipboardService = new();

        public MemoryStream Stream { get; } = new();

        public CommandRouter Router => new(_inputInjector, _clipboardService);

        public ClientSession CreateSession()
        {
            return new ClientSession(Stream);
        }

        public void Dispose()
        {
            _clipboardService.Dispose();
            _inputInjector.Dispose();
        }
    }
}
