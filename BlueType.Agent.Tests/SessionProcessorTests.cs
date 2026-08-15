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
        var responses = await EnvelopeTestReader.ReadAllAsync(harness.GetWrittenBytes());

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
        var responses = await EnvelopeTestReader.ReadAllAsync(harness.GetWrittenBytes());

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
        var responses = await EnvelopeTestReader.ReadAllAsync(harness.GetWrittenBytes());

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
        var responses = await EnvelopeTestReader.ReadAllAsync(harness.GetWrittenBytes());

        Assert.Equal(2, responses.Count);
        Assert.Equal(Responses.AuthResult, responses[0].Type);
        Assert.Equal(Responses.Error, responses[1].Type);
        Assert.Equal("SERVER_ERROR", EnvelopeTestReader.GetString(responses[1], "code"));
        Assert.Contains("Unsupported mouse button action", EnvelopeTestReader.GetString(responses[1], "message"));
    }

    [Fact]
    public async Task RunAsync_ReportsClientConnected_AndEndsWhenEnvelopeIsNull()
    {
        using var harness = ProcessorHarness.CreateEmpty();
        var states = new List<ConnectionState>();
        await using var session = harness.CreateSession();

        await harness.Processor.RunAsync(
            session,
            "127.0.0.1:24862",
            "wifi",
            states.Add,
            null,
            CancellationToken.None);

        Assert.Collection(states, state => Assert.Equal(ConnectionState.ClientConnected, state));
        var responses = await EnvelopeTestReader.ReadAllAsync(harness.GetWrittenBytes());

        Assert.Empty(responses);
    }

    [Fact]
    public async Task RunAsync_HandlesHeartbeatWithoutRoutingItAsACommand()
    {
        var ping = JsonProtocol.CreateEnvelope("ping-1", Commands.Ping, new { });
        using var harness = await ProcessorHarness.CreateAsync(ping);
        await using var session = harness.CreateSession();

        await harness.Processor.RunAsync(
            session,
            "127.0.0.1:24862",
            "wifi",
            null,
            null,
            CancellationToken.None);

        var responses = await EnvelopeTestReader.ReadAllAsync(harness.GetWrittenBytes());

        Assert.Single(responses);
        Assert.Equal(Commands.Pong, responses[0].Type);
        Assert.Equal("ping-1", responses[0].Id);
    }

    [Fact]
    public async Task RunAsync_WritesAuthResult_WhenHelloIsAuthorized()
    {
        using var sandbox = new DeviceRegistrySandbox();
        var registry = new DeviceRegistry(sandbox.SettingsFilePath);
        var authService = new AuthService(registry, (_, _) => Task.FromResult(AuthPromptDecision.AlwaysAllow));
        var approval = await authService.RequestApprovalAsync(
            new HelloInfo("device-auth", "Pixel", "1.0.0"),
            "127.0.0.1:24862",
            "wifi",
            CancellationToken.None);
        var hello = JsonProtocol.CreateEnvelope(
            "hello-auth",
            Commands.Hello,
            new { deviceId = "device-auth", deviceName = "Pixel", appVersion = "1.0.0" },
            approval.Token);

        using var harness = await ProcessorHarness.CreateAsync(authService, hello);
        await using var session = harness.CreateSession();

        await harness.Processor.RunAsync(
            session,
            "127.0.0.1:24862",
            "wifi",
            null,
            null,
            CancellationToken.None);

        var responses = await EnvelopeTestReader.ReadAllAsync(harness.GetWrittenBytes());

        Assert.Equal(2, responses.Count);
        Assert.Equal(Responses.AuthResult, responses[1].Type);
        Assert.True(EnvelopeTestReader.GetBoolean(responses[1], "ok"));
    }

    [Fact]
    public async Task RunAsync_WritesSessionReplacedError_AndEndsTheLoop()
    {
        using var sandbox = new DeviceRegistrySandbox();
        var registry = new DeviceRegistry(sandbox.SettingsFilePath);
        var authService = new AuthService(registry, (_, _) => Task.FromResult(AuthPromptDecision.AlwaysAllow));
        var approval = await authService.RequestApprovalAsync(
            new HelloInfo("device-replaced", "Pixel", "1.0.0"),
            "127.0.0.1:24862",
            "wifi",
            CancellationToken.None);
        var sessionId = Guid.NewGuid();
        var hello = JsonProtocol.CreateEnvelope(
            "hello-replaced",
            Commands.Hello,
            new { deviceId = "device-replaced", deviceName = "Pixel", appVersion = "1.0.0" },
            approval.Token);
        var command = JsonProtocol.CreateEnvelope(
            "key-replaced",
            Commands.KeyTap,
            new { key = "ENTER" },
            approval.Token);
        var activeSessions = new ActiveSessionManager();
        using var harness = await ProcessorHarness.CreateAsync(authService, activeSessions, hello, command);
        await using var session = harness.CreateSession();

        await harness.Processor.RunAsync(
            session,
            "127.0.0.1:24862",
            "wifi",
            state =>
            {
                if (state == ConnectionState.Connected)
                {
                    activeSessions.Deactivate(sessionId);
                }
            },
            null,
            sessionId,
            () => { },
            CancellationToken.None);

        var responses = await EnvelopeTestReader.ReadAllAsync(harness.GetWrittenBytes());

        Assert.Equal(2, responses.Count);
        Assert.Equal(Responses.Error, responses[1].Type);
        Assert.Equal("SESSION_REPLACED", EnvelopeTestReader.GetString(responses[1], "code"));
    }

    [Fact]
    public async Task RunAsync_CleanupContinuesWhenKeyboardReleaseFails()
    {
        var sessionId = Guid.NewGuid();
        var activeSessions = new ActiveSessionManager();
        await activeSessions.ActivateAsync(
            new ActiveSessionCandidate(
                sessionId,
                "device-cleanup",
                "Cleanup Device",
                "wifi",
                "127.0.0.1:24862",
                () => { }),
            (_, _) => Task.FromResult(true),
            CancellationToken.None);

        var inputRelease = new RecordingInputRelease
        {
            ThrowOnKeyboardRelease = true,
        };
        var shortcutProfiles = new RecordingShortcutProfileDispatcher();
        using var harness = ProcessorHarness.CreateEmpty(activeSessions, shortcutProfiles, inputRelease);
        await using var session = harness.CreateSession();

        await harness.Processor.RunAsync(
            session,
            "127.0.0.1:24862",
            "wifi",
            null,
            null,
            sessionId,
            () => { },
            CancellationToken.None);

        Assert.Equal(1, shortcutProfiles.UnregisterCount);
        Assert.False(activeSessions.IsActive(sessionId));
        Assert.Equal(1, inputRelease.KeyboardReleaseCount);
        Assert.Equal(1, inputRelease.MouseReleaseCount);
    }

    [Fact]
    public async Task RunAsync_CancellationCompletesCleanup()
    {
        using var stream = new BlockingDuplexStream();
        using var harness = ProcessorHarness.Create(stream);
        await using var session = harness.CreateSession();
        using var cancellation = new CancellationTokenSource();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = harness.Processor.RunAsync(
            session,
            "127.0.0.1:24862",
            "wifi",
            state =>
            {
                if (state == ConnectionState.ClientConnected)
                {
                    connected.TrySetResult();
                }
            },
            null,
            cancellation.Token);

        await connected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await runTask;

        Assert.Equal(1, harness.InputRelease.KeyboardReleaseCount);
        Assert.Equal(1, harness.InputRelease.MouseReleaseCount);
    }

    [Fact]
    public async Task RunAsync_HeartbeatTimeoutEndsSessionAndRunsCleanup()
    {
        using var stream = new BlockingDuplexStream();
        var heartbeat = new SessionHeartbeat(TimeSpan.FromMilliseconds(10), TimeSpan.Zero);
        using var harness = ProcessorHarness.Create(stream, heartbeat: heartbeat);
        await using var session = harness.CreateSession();
        var messages = new List<string>();

        await harness.Processor.RunAsync(
            session,
            "127.0.0.1:24862",
            "wifi",
            null,
            messages.Add,
            CancellationToken.None);

        Assert.Contains(messages, message => message.Contains("timed out", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, harness.InputRelease.KeyboardReleaseCount);
        Assert.Equal(1, harness.InputRelease.MouseReleaseCount);
    }

    private sealed class ProcessorHarness : IDisposable
    {
        private readonly InputInjector _inputInjector = new();
        private readonly ClipboardService _clipboardService = new();

        private ProcessorHarness(
            Stream stream,
            AuthService? authService = null,
            ActiveSessionManager? activeSessions = null,
            RecordingShortcutProfileDispatcher? shortcutProfiles = null,
            RecordingInputRelease? inputRelease = null,
            SessionHeartbeat? heartbeat = null)
        {
            Stream = stream;
            AuthService = authService ?? new AuthService(new DeviceRegistry(), (_, _) => Task.FromResult(AuthPromptDecision.AllowOnce));
            ActiveSessions = activeSessions ?? new ActiveSessionManager();
            ShortcutProfiles = shortcutProfiles ?? new RecordingShortcutProfileDispatcher();
            InputRelease = inputRelease ?? new RecordingInputRelease();
            Router = new CommandRouter(_inputInjector, _clipboardService);
            Processor = new SessionProcessor(
                Router,
                AuthService,
                _inputInjector,
                ActiveSessions,
                ShortcutProfiles,
                (_, _) => Task.FromResult(AuthPromptDecision.Deny),
                InputRelease,
                heartbeat ?? new SessionHeartbeat());
        }

        public Stream Stream { get; }

        public CommandRouter Router { get; }

        public AuthService AuthService { get; }

        public ActiveSessionManager ActiveSessions { get; }

        public RecordingShortcutProfileDispatcher ShortcutProfiles { get; }

        public RecordingInputRelease InputRelease { get; }

        public SessionProcessor Processor { get; }

        public ClientSession CreateSession()
        {
            return new ClientSession(Stream);
        }

        public byte[] GetWrittenBytes()
        {
            return ((ScriptedDuplexStream)Stream).GetWrittenBytes();
        }

        public static Task<ProcessorHarness> CreateAsync(params Envelope[] inboundFrames)
        {
            return CreateAsync(inboundFrames, authService: null, activeSessions: null);
        }

        public static Task<ProcessorHarness> CreateAsync(AuthService authService, params Envelope[] inboundFrames)
        {
            return CreateAsync(inboundFrames, authService, activeSessions: null);
        }

        public static Task<ProcessorHarness> CreateAsync(
            AuthService authService,
            ActiveSessionManager activeSessions,
            params Envelope[] inboundFrames)
        {
            return CreateAsync(inboundFrames, authService, activeSessions);
        }

        public static ProcessorHarness CreateEmpty(
            ActiveSessionManager? activeSessions = null,
            RecordingShortcutProfileDispatcher? shortcutProfiles = null,
            RecordingInputRelease? inputRelease = null,
            SessionHeartbeat? heartbeat = null)
        {
            return new ProcessorHarness(
                new ScriptedDuplexStream(),
                activeSessions: activeSessions,
                shortcutProfiles: shortcutProfiles,
                inputRelease: inputRelease,
                heartbeat: heartbeat);
        }

        public static ProcessorHarness Create(
            Stream stream,
            ActiveSessionManager? activeSessions = null,
            RecordingShortcutProfileDispatcher? shortcutProfiles = null,
            RecordingInputRelease? inputRelease = null,
            SessionHeartbeat? heartbeat = null)
        {
            return new ProcessorHarness(stream, activeSessions: activeSessions, shortcutProfiles: shortcutProfiles, inputRelease: inputRelease, heartbeat: heartbeat);
        }

        private static async Task<ProcessorHarness> CreateAsync(
            Envelope[] inboundFrames,
            AuthService? authService,
            ActiveSessionManager? activeSessions)
        {
            var stream = new ScriptedDuplexStream(await SerializeAsync(inboundFrames));
            return new ProcessorHarness(stream, authService, activeSessions);
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

    private sealed class RecordingInputRelease : IInputRelease
    {
        public bool ThrowOnKeyboardRelease { get; init; }

        public int KeyboardReleaseCount => Volatile.Read(ref _keyboardReleaseCount);

        public int MouseReleaseCount => Volatile.Read(ref _mouseReleaseCount);

        private int _keyboardReleaseCount;
        private int _mouseReleaseCount;

        public Task ReleaseAllKeysAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _keyboardReleaseCount);
            return ThrowOnKeyboardRelease
                ? Task.FromException(new InvalidOperationException("Keyboard release failed."))
                : Task.CompletedTask;
        }

        public Task ReleaseAllMouseButtonsAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _mouseReleaseCount);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingShortcutProfileDispatcher : IShortcutProfileDispatcher
    {
        public int RegisterCount => Volatile.Read(ref _registerCount);

        public int UnregisterCount => Volatile.Read(ref _unregisterCount);

        private int _registerCount;
        private int _unregisterCount;

        public void RegisterSession(Guid sessionId, ClientSession session)
        {
            Interlocked.Increment(ref _registerCount);
        }

        public void UnregisterSession(Guid sessionId)
        {
            Interlocked.Increment(ref _unregisterCount);
        }
    }

    private sealed class BlockingDuplexStream : ScriptedDuplexStream
    {
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(WaitForCloseAsync(cancellationToken));
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return WaitForCloseAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closed.TrySetResult();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            _closed.TrySetResult();
            await base.DisposeAsync();
        }

        private async Task<int> WaitForCloseAsync(CancellationToken cancellationToken)
        {
            var cancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var completed = await Task.WhenAny(_closed.Task, cancellation);
            if (completed == _closed.Task)
            {
                return 0;
            }

            await cancellation;
            return 0;
        }
    }
}
