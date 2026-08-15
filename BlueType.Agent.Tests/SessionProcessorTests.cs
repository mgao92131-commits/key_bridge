using BlueType.Agent.Core;
using BlueType.Agent.Application.Commands;
using BlueType.Agent.Application.Authorization;
using BlueType.Agent.Application.Sessions;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Domain.Devices;
using BlueType.Agent.Models;
using BlueType.Agent.Tests.TestHelpers;
using BlueType.Agent.Transport;
using BlueType.Protocol;

namespace BlueType.Agent.Tests;

public sealed class SessionProcessorTests
{
    [Fact]
    public async Task RejectBusyClientAsync_WritesBusyError()
    {
        using var harness = ProcessorHarness.CreateEmpty();
        await using var session = harness.CreateSession();

        await harness.Processor.RejectBusyClientAsync(session, CancellationToken.None);
        var responses = await EnvelopeTestReader.ReadAllAsync(harness.Stream.GetWrittenBytes());

        Assert.Single(responses);
        Assert.Equal(Responses.Error, responses[0].Type);
        Assert.Equal("BUSY", EnvelopeTestReader.GetString(responses[0], "code"));
    }

    [Fact]
    public async Task RunAsync_WritesNotAuthorizedError_WhenCommandArrivesBeforeHello()
    {
        var command = JsonProtocol.CreateEnvelope("key-1", Commands.KeyTap, new { key = "ENTER" });
        using var harness = await ProcessorHarness.CreateAsync(command);
        var states = new List<ConnectionState>();
        await using var session = harness.CreateSession();

        await harness.Processor.RunAsync(session, "127.0.0.1:24862", "wifi", states.Add, null, CancellationToken.None);
        var responses = await EnvelopeTestReader.ReadAllAsync(harness.Stream.GetWrittenBytes());

        Assert.Collection(states, state => Assert.Equal(ConnectionState.ClientConnected, state));
        Assert.Single(responses);
        Assert.Equal(Responses.Error, responses[0].Type);
        Assert.Equal("NOT_AUTHORIZED", EnvelopeTestReader.GetString(responses[0], "code"));
    }

    [Fact]
    public async Task RunAsync_WritesAuthResultThenDuplicateHelloError_WhenHelloRepeats()
    {
        using var sandbox = new DeviceRegistrySandbox();
        var registry = new DeviceRegistry(sandbox.SettingsFilePath);
        var authService = new AuthService(registry, (_, _) => Task.FromResult(AuthPromptDecision.AlwaysAllow));
        var approval = await authService.RequestApprovalAsync(
            new HelloInfo("device-processor", "Pixel", "1.0.0"),
            "127.0.0.1:24862",
            "wifi",
            CancellationToken.None);

        var firstHello = JsonProtocol.CreateEnvelope(
            "hello-1",
            Commands.Hello,
            new { deviceId = "device-processor", deviceName = "Pixel", appVersion = "1.0.0" },
            approval.Token);
        var secondHello = JsonProtocol.CreateEnvelope(
            "hello-2",
            Commands.Hello,
            new { deviceId = "device-processor", deviceName = "Pixel", appVersion = "1.0.0" },
            approval.Token);

        using var harness = await ProcessorHarness.CreateAsync(authService, firstHello, secondHello);
        var states = new List<ConnectionState>();
        await using var session = harness.CreateSession();

        await harness.Processor.RunAsync(session, "127.0.0.1:24862", "wifi", states.Add, null, CancellationToken.None);
        var responses = await EnvelopeTestReader.ReadAllAsync(harness.Stream.GetWrittenBytes());

        Assert.Collection(
            states,
            state => Assert.Equal(ConnectionState.ClientConnected, state),
            state => Assert.Equal(ConnectionState.Authenticating, state),
            state => Assert.Equal(ConnectionState.Connected, state));
        Assert.Equal(2, responses.Count);
        Assert.Equal(Responses.AuthResult, responses[0].Type);
        Assert.Equal(Responses.Error, responses[1].Type);
        Assert.Equal("INVALID_PAYLOAD", EnvelopeTestReader.GetString(responses[1], "code"));
        Assert.Equal("HELLO already completed.", EnvelopeTestReader.GetString(responses[1], "message"));
    }

    [Fact]
    public async Task RunAsync_ReturnsServerError_WhenAuthorizedCommandHandlerThrows()
    {
        using var sandbox = new DeviceRegistrySandbox();
        var registry = new DeviceRegistry(sandbox.SettingsFilePath);
        var authService = new AuthService(registry, (_, _) => Task.FromResult(AuthPromptDecision.AlwaysAllow));
        var approval = await authService.RequestApprovalAsync(
            new HelloInfo("device-server-error", "Pixel", "1.0.0"),
            "127.0.0.1:24862",
            "wifi",
            CancellationToken.None);

        var hello = JsonProtocol.CreateEnvelope(
            "hello-server-error",
            Commands.Hello,
            new { deviceId = "device-server-error", deviceName = "Pixel", appVersion = "1.0.0" },
            approval.Token);
        var invalidMouseButton = JsonProtocol.CreateEnvelope(
            "mouse-bad",
            Commands.MouseButton,
            new { button = "LEFT", action = "sideways" },
            approval.Token);

        using var harness = await ProcessorHarness.CreateAsync(authService, hello, invalidMouseButton);
        await using var session = harness.CreateSession();

        await harness.Processor.RunAsync(session, "127.0.0.1:24862", "wifi", null, null, CancellationToken.None);
        var responses = await EnvelopeTestReader.ReadAllAsync(harness.Stream.GetWrittenBytes());

        Assert.Equal(2, responses.Count);
        Assert.Equal(Responses.AuthResult, responses[0].Type);
        Assert.Equal(Responses.Error, responses[1].Type);
        Assert.Equal("SERVER_ERROR", EnvelopeTestReader.GetString(responses[1], "code"));
        Assert.Contains("Unsupported mouse button action", EnvelopeTestReader.GetString(responses[1], "message"));
    }

    private sealed class ProcessorHarness : IDisposable
    {
        private readonly InputInjector _inputInjector = new();
        private readonly ClipboardService _clipboardService = new();

        private ProcessorHarness(ScriptedDuplexStream stream, AuthService? authService = null)
        {
            Stream = stream;
            AuthService = authService ?? new AuthService(new DeviceRegistry(), (_, _) => Task.FromResult(AuthPromptDecision.AllowOnce));
            Router = new CommandRouter(_inputInjector, _clipboardService);
            Processor = new SessionProcessor(Router, AuthService, _inputInjector);
        }

        public ScriptedDuplexStream Stream { get; }

        public CommandRouter Router { get; }

        public AuthService AuthService { get; }

        public SessionProcessor Processor { get; }

        public ClientSession CreateSession()
        {
            return new ClientSession(Stream);
        }

        public static async Task<ProcessorHarness> CreateAsync(params Envelope[] inboundFrames)
        {
            return await CreateAsync(inboundFrames, authService: null);
        }

        public static async Task<ProcessorHarness> CreateAsync(AuthService authService, params Envelope[] inboundFrames)
        {
            return await CreateAsync(inboundFrames, authService);
        }

        public static ProcessorHarness CreateEmpty()
        {
            return new ProcessorHarness(new ScriptedDuplexStream());
        }

        public static async Task<ProcessorHarness> CreateAsync(Envelope[] inboundFrames, AuthService? authService)
        {
            var stream = new ScriptedDuplexStream(await SerializeAsync(inboundFrames));
            return new ProcessorHarness(stream, authService);
        }

        public void Dispose()
        {
            Stream.Dispose();
            _clipboardService.Dispose();
            _inputInjector.Dispose();
        }

        private static async Task<byte[]> SerializeAsync(IEnumerable<Envelope> envelopes)
        {
            await using var buffer = new MemoryStream();
            foreach (var envelope in envelopes)
            {
                await FrameCodec.WriteAsync(buffer, envelope);
            }

            return buffer.ToArray();
        }
    }
}
