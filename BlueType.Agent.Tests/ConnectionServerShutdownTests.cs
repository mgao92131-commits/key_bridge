using BlueType.Agent.Core;
using BlueType.Agent.Application.Commands;
using BlueType.Agent.Application.Authorization;
using BlueType.Agent.Domain.Devices;
using BlueType.Agent.Application.Sessions;
using BlueType.Agent.Infrastructure.Clipboard;
using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Models;
using BlueType.Agent.Tests.TestHelpers;
using BlueType.Agent.Transport;
using BlueType.Protocol;

namespace BlueType.Agent.Tests;

public sealed class ConnectionServerShutdownTests
{
    [Fact]
    public async Task StopAsync_IdleAcceptLoop_CompletesQuickly()
    {
        using var harness = ServerHarness.Create();
        var server = new FakeConnectionServer(harness.Processor, acceptMode: AcceptMode.CancelableIdle);
        server.Start();

        var stopTask = server.StopAsync();
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.Same(stopTask, completed);
        await stopTask;
    }

    [Fact]
    public async Task StopAsync_ConnectedSession_CancelsAndCompletes()
    {
        using var harness = ServerHarness.Create();
        var server = new FakeConnectionServer(harness.Processor, acceptMode: AcceptMode.SingleHangingClient);
        server.Start();

        Assert.True(await server.WaitForSessionStartAsync(TimeSpan.FromSeconds(2)));

        var stopTask = server.StopAsync();
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(stopTask, completed);
        await stopTask;
        Assert.True(server.LastClientClosed);
    }

    [Fact]
    public async Task StopAsync_BlockingAcceptThatIgnoresCancel_DoesNotHangForever()
    {
        using var harness = ServerHarness.Create();
        var server = new FakeConnectionServer(
            harness.Processor,
            acceptMode: AcceptMode.BlockingIgnoreCancel,
            listenLoopShutdownTimeout: TimeSpan.FromMilliseconds(200));
        server.Start();

        // Give accept loop time to enter the blocking wait.
        await Task.Delay(50);

        var startedAt = Environment.TickCount64;
        await server.StopAsync();
        var elapsed = Environment.TickCount64 - startedAt;

        Assert.True(elapsed < 2000, $"StopAsync took {elapsed} ms, expected bound near timeout.");
        Assert.True(server.StopListeningCalled);
    }

    [Fact]
    public async Task StopAsync_ActiveAuthorizationWait_CompletesAfterCancel()
    {
        var approvalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var approvalGate = new TaskCompletionSource<AuthPromptDecision>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var harness = ServerHarness.Create(async (_, cancellationToken) =>
        {
            approvalStarted.TrySetResult();
            using var registration = cancellationToken.Register(() =>
                approvalGate.TrySetCanceled(cancellationToken));
            return await approvalGate.Task;
        });

        var server = new FakeConnectionServer(harness.Processor, acceptMode: AcceptMode.SingleHelloClient);
        server.Start();

        await approvalStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stopTask = server.StopAsync();
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(stopTask, completed);
        await stopTask;
    }

    [Fact]
    public async Task StopAsync_MultipleSessions_WaitsForAllTrackedTasks()
    {
        using var harness = ServerHarness.Create();
        var server = new FakeConnectionServer(harness.Processor, acceptMode: AcceptMode.ThreeHangingClients);
        server.Start();

        Assert.True(await server.WaitForActiveClientCountAsync(3, TimeSpan.FromSeconds(2)));

        await server.StopAsync();

        Assert.Equal(0, server.TrackedSessionCount);
        Assert.Equal(0, server.ActiveClientCount);
        Assert.Equal(3, server.ClosedClientCount);
    }

    [Fact]
    public async Task StopAsync_IsIdempotent()
    {
        using var harness = ServerHarness.Create();
        var server = new FakeConnectionServer(harness.Processor, acceptMode: AcceptMode.CancelableIdle);
        server.Start();

        await server.StopAsync();
        await server.StopAsync();
        server.Dispose();
        server.Dispose();
    }

    private enum AcceptMode
    {
        CancelableIdle,
        BlockingIgnoreCancel,
        SingleHangingClient,
        SingleHelloClient,
        ThreeHangingClients,
    }

    private sealed class FakeConnectionServer : ConnectionServerBase<FakeClient>
    {
        private readonly AcceptMode _acceptMode;
        private readonly TimeSpan _listenLoopShutdownTimeout;
        private readonly Queue<FakeClient> _pendingClients = new();
        private readonly object _gate = new();
        private int _sessionsStarted;
        private int _closedClients;
        private int _acceptIndex;
        private volatile bool _listeningStopped;

        public FakeConnectionServer(
            SessionProcessor processor,
            AcceptMode acceptMode,
            TimeSpan? listenLoopShutdownTimeout = null)
            : base(processor)
        {
            _acceptMode = acceptMode;
            _listenLoopShutdownTimeout = listenLoopShutdownTimeout ?? TimeSpan.FromSeconds(2);

            switch (acceptMode)
            {
                case AcceptMode.SingleHangingClient:
                    _pendingClients.Enqueue(new FakeClient(new HangUntilCanceledStream()));
                    break;
                case AcceptMode.SingleHelloClient:
                    _pendingClients.Enqueue(new FakeClient(CreateHelloOnlyStream()));
                    break;
                case AcceptMode.ThreeHangingClients:
                    _pendingClients.Enqueue(new FakeClient(new HangUntilCanceledStream()));
                    _pendingClients.Enqueue(new FakeClient(new HangUntilCanceledStream()));
                    _pendingClients.Enqueue(new FakeClient(new HangUntilCanceledStream()));
                    break;
            }
        }

        public bool StopListeningCalled => _listeningStopped;

        public bool LastClientClosed => Volatile.Read(ref _closedClients) > 0;

        public int ClosedClientCount => Volatile.Read(ref _closedClients);

        protected override string TransportDisplayName => "Fake";

        protected override string SessionTransportName => "fake";

        protected override TimeSpan ListenLoopShutdownTimeout => _listenLoopShutdownTimeout;

        protected override TimeSpan SessionShutdownTimeout => TimeSpan.FromSeconds(2);

        public void Start()
        {
            StartAcceptLoop("Fake server listening.", "Fake server listening.");
        }

        public async Task<bool> WaitForSessionStartAsync(TimeSpan timeout)
        {
            var startedAt = Environment.TickCount64;
            while (Environment.TickCount64 - startedAt < timeout.TotalMilliseconds)
            {
                if (Volatile.Read(ref _sessionsStarted) > 0)
                {
                    return true;
                }

                await Task.Delay(20);
            }

            return false;
        }

        public async Task<bool> WaitForSessionCountAsync(int count, TimeSpan timeout)
        {
            var startedAt = Environment.TickCount64;
            while (Environment.TickCount64 - startedAt < timeout.TotalMilliseconds)
            {
                if (Volatile.Read(ref _sessionsStarted) >= count)
                {
                    return true;
                }

                await Task.Delay(20);
            }

            return false;
        }

        public async Task<bool> WaitForActiveClientCountAsync(int count, TimeSpan timeout)
        {
            var startedAt = Environment.TickCount64;
            while (Environment.TickCount64 - startedAt < timeout.TotalMilliseconds)
            {
                if (ActiveClientCount >= count)
                {
                    return true;
                }

                await Task.Delay(20);
            }

            return false;
        }

        protected override async Task<FakeClient> AcceptClientAsync(CancellationToken cancellationToken)
        {
            switch (_acceptMode)
            {
                case AcceptMode.CancelableIdle:
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    throw new OperationCanceledException(cancellationToken);

                case AcceptMode.BlockingIgnoreCancel:
                    // Simulate a native Accept that ignores CancellationToken and is not
                    // unblocked by StopListening — StopAsync must still bound its wait.
                    await Task.Delay(Timeout.InfiniteTimeSpan);
                    throw new InvalidOperationException("Blocking accept should not resume.");

                case AcceptMode.SingleHangingClient:
                case AcceptMode.SingleHelloClient:
                case AcceptMode.ThreeHangingClients:
                    lock (_gate)
                    {
                        if (_pendingClients.Count > 0)
                        {
                            Interlocked.Increment(ref _sessionsStarted);
                            return _pendingClients.Dequeue();
                        }
                    }

                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    throw new OperationCanceledException(cancellationToken);

                default:
                    throw new InvalidOperationException($"Unknown accept mode {_acceptMode}.");
            }
        }

        protected override ClientSession CreateSession(FakeClient client)
        {
            return new ClientSession(client.Stream);
        }

        protected override void CloseClient(FakeClient? client)
        {
            if (client is null)
            {
                return;
            }

            if (client.TryClose())
            {
                Interlocked.Increment(ref _closedClients);
            }
        }

        protected override void DisposeClient(FakeClient client)
        {
            client.Dispose();
        }

        protected override string? GetRemoteAddress(FakeClient client)
        {
            return $"fake-{Interlocked.Increment(ref _acceptIndex)}";
        }

        protected override void StopListening()
        {
            _listeningStopped = true;
        }

        private static Stream CreateHelloOnlyStream()
        {
            // Empty inbound frames: ReadAsync returns null/EOF quickly after hello path... 
            // Use a stream that blocks on first read until canceled so approval path can run.
            // Actually hello needs inbound data. For auth wait test we use prompt that blocks;
            // session ReadAsync needs a hello envelope first. Simpler: hang stream + custom
            // processor already wired with blocking prompt — but RunAsync won't call prompt
            // until hello arrives.
            //
            // Provide a one-shot hello frame then hang.
            return new HelloThenHangStream();
        }
    }

    private sealed class FakeClient : IDisposable
    {
        private int _closed;

        public FakeClient(Stream stream)
        {
            Stream = stream;
        }

        public Stream Stream { get; }

        public bool TryClose()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return false;
            }

            Stream.Dispose();
            return true;
        }

        public void Close()
        {
            TryClose();
        }

        public void Dispose()
        {
            Close();
        }
    }

    private sealed class HangUntilCanceledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class HelloThenHangStream : Stream
    {
        private readonly byte[] _helloFrame;
        private int _helloOffset;
        private bool _helloConsumed;

        public HelloThenHangStream()
        {
            var hello = JsonProtocol.CreateEnvelope(
                "hello-shutdown",
                Commands.Hello,
                new { deviceId = "device-shutdown", deviceName = "Pixel", appVersion = "1.0.0" });
            using var buffer = new MemoryStream();
            FrameCodec.WriteAsync(buffer, hello).GetAwaiter().GetResult();
            _helloFrame = buffer.ToArray();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_helloConsumed)
            {
                var remaining = _helloFrame.Length - _helloOffset;
                if (remaining > 0)
                {
                    var toCopy = Math.Min(remaining, buffer.Length);
                    _helloFrame.AsSpan(_helloOffset, toCopy).CopyTo(buffer.Span);
                    _helloOffset += toCopy;
                    if (_helloOffset >= _helloFrame.Length)
                    {
                        _helloConsumed = true;
                    }

                    return toCopy;
                }

                _helloConsumed = true;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class ServerHarness : IDisposable
    {
        private readonly InputInjector _inputInjector = new();
        private readonly ClipboardService _clipboardService = new();
        private readonly DeviceRegistrySandbox _sandbox = new();

        public SessionProcessor Processor { get; }

        private ServerHarness(Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> prompt)
        {
            var registry = new DeviceRegistry(_sandbox.SettingsFilePath);
            var authService = new AuthService(registry, prompt);
            var router = new CommandRouter(_inputInjector, _clipboardService);
            Processor = new SessionProcessor(router, authService, _inputInjector);
        }

        public static ServerHarness Create()
        {
            return new ServerHarness((_, _) => Task.FromResult(AuthPromptDecision.Deny));
        }

        public static ServerHarness Create(Func<AuthPromptRequest, CancellationToken, Task<AuthPromptDecision>> prompt)
        {
            return new ServerHarness(prompt);
        }

        public void Dispose()
        {
            _clipboardService.Dispose();
            _inputInjector.Dispose();
            _sandbox.Dispose();
        }
    }
}
