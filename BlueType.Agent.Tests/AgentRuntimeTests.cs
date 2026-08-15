using BlueType.Agent.Application.Sessions;
using BlueType.Agent.Host;
using BlueType.Agent.Models;
using BlueType.Agent.Transport;

namespace BlueType.Agent.Tests;

public sealed class AgentRuntimeTests
{
    [Fact]
    public async Task Start_StartsBothTransportsAndEntersRunningState()
    {
        using var fixture = RuntimeFixture.Create();

        fixture.Runtime.Start();

        Assert.Equal(RuntimeState.Running, fixture.Runtime.State);
        Assert.Equal(1, fixture.Tcp.StartCallCount);
        Assert.Equal(1, fixture.Bluetooth.StartCallCount);

        await fixture.Runtime.StopAsync();

        Assert.Equal(RuntimeState.Stopped, fixture.Runtime.State);
    }

    [Fact]
    public async Task Start_Twice_ThrowsAndDoesNotStartTransportsAgain()
    {
        using var fixture = RuntimeFixture.Create();

        fixture.Runtime.Start();

        var exception = Assert.Throws<InvalidOperationException>(() => fixture.Runtime.Start());

        Assert.Contains("Running", exception.Message);
        Assert.Equal(1, fixture.Tcp.StartCallCount);
        Assert.Equal(1, fixture.Bluetooth.StartCallCount);

        await fixture.Runtime.StopAsync();
    }

    [Fact]
    public async Task Start_WhenBluetoothFails_RollsBackTcpAndStopsRuntime()
    {
        using var fixture = RuntimeFixture.Create(
            bluetoothStartException: new InvalidOperationException("Bluetooth start failed."));

        var exception = Assert.Throws<InvalidOperationException>(() => fixture.Runtime.Start());

        Assert.Equal("Bluetooth start failed.", exception.Message);
        Assert.Equal(RuntimeState.Stopped, fixture.Runtime.State);
        Assert.Equal(1, fixture.Tcp.StartCallCount);
        Assert.Equal(1, fixture.Bluetooth.StartCallCount);
        Assert.Equal(1, fixture.Tcp.StopCallCount);
        Assert.Equal(1, fixture.Bluetooth.StopCallCount);
        Assert.Equal(1, fixture.Input.DisposeCallCount);
        Assert.Equal(1, fixture.Clipboard.DisposeCallCount);
        Assert.Equal(1, fixture.ShortcutProfiles.DisposeCallCount);
        Assert.Equal(1, fixture.Tcp.DisposeCallCount);
        Assert.Equal(1, fixture.Bluetooth.DisposeCallCount);

        await fixture.Runtime.StopAsync();

        Assert.Equal(1, fixture.Tcp.StopCallCount);
        Assert.Equal(1, fixture.Bluetooth.StopCallCount);
    }

    [Fact]
    public async Task Start_AfterStop_ThrowsAndDoesNotRestartTransports()
    {
        using var fixture = RuntimeFixture.Create();

        await fixture.Runtime.StopAsync();

        var exception = Assert.Throws<InvalidOperationException>(() => fixture.Runtime.Start());

        Assert.Contains("Stopped", exception.Message);
        Assert.Equal(0, fixture.Tcp.StartCallCount);
        Assert.Equal(0, fixture.Bluetooth.StartCallCount);
    }

    [Fact]
    public async Task StopAsync_WithoutStart_CompletesWithinTimeout()
    {
        using var fixture = RuntimeFixture.Create();

        var stopTask = fixture.Runtime.StopAsync();
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.Same(stopTask, completed);
        await stopTask;
        Assert.Equal(RuntimeState.Stopped, fixture.Runtime.State);
    }

    [Fact]
    public async Task StopAsync_IsIdempotent()
    {
        using var fixture = RuntimeFixture.Create();

        var firstStop = fixture.Runtime.StopAsync();
        var secondStop = fixture.Runtime.StopAsync();

        Assert.Same(firstStop, secondStop);
        await firstStop;

        Assert.Equal(1, fixture.Tcp.StopCallCount);
        Assert.Equal(1, fixture.Bluetooth.StopCallCount);
        Assert.Equal(1, fixture.Input.DisposeCallCount);
        Assert.Equal(1, fixture.Clipboard.DisposeCallCount);
        Assert.Equal(1, fixture.ShortcutProfiles.DisposeCallCount);
    }

    [Fact]
    public async Task StopAsync_StopsBothTransports()
    {
        using var fixture = RuntimeFixture.Create();

        await fixture.Runtime.StopAsync();

        Assert.Equal(1, fixture.Tcp.StopCallCount);
        Assert.Equal(1, fixture.Bluetooth.StopCallCount);
        Assert.Equal(1, fixture.Tcp.DisposeCallCount);
        Assert.Equal(1, fixture.Bluetooth.DisposeCallCount);
    }

    [Fact]
    public async Task StopAsync_WhenTransportStopFails_StillDisposesResources()
    {
        using var fixture = RuntimeFixture.Create(
            bluetoothStopException: new InvalidOperationException("Bluetooth stop failed."));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Runtime.StopAsync());

        Assert.Equal("Bluetooth stop failed.", exception.Message);
        Assert.Equal(1, fixture.Tcp.StopCallCount);
        Assert.Equal(1, fixture.Bluetooth.StopCallCount);
        Assert.Equal(1, fixture.Input.DisposeCallCount);
        Assert.Equal(1, fixture.Clipboard.DisposeCallCount);
        Assert.Equal(1, fixture.ShortcutProfiles.DisposeCallCount);
        Assert.Equal(1, fixture.Tcp.DisposeCallCount);
        Assert.Equal(1, fixture.Bluetooth.DisposeCallCount);
    }

    [Fact]
    public async Task DisconnectActiveClient_PrefersActiveSession()
    {
        using var fixture = RuntimeFixture.Create();
        var sessionDisconnects = 0;
        var sessionId = Guid.NewGuid();

        var activation = await fixture.Sessions.ActivateAsync(
            new ActiveSessionCandidate(
                sessionId,
                "device-id",
                "Device",
                "wifi",
                "127.0.0.1",
                () => Interlocked.Increment(ref sessionDisconnects)),
            (_, _) => Task.FromResult(true),
            CancellationToken.None);

        Assert.True(activation.IsActivated);
        Assert.True(fixture.Runtime.DisconnectActiveClient());
        Assert.Equal(1, sessionDisconnects);
        Assert.Equal(0, fixture.Tcp.DisconnectCallCount);
        Assert.Equal(0, fixture.Bluetooth.DisconnectCallCount);

        await fixture.Runtime.StopAsync();
    }

    [Fact]
    public async Task DisconnectActiveClient_WithoutActiveSession_FallsBackToTransportClient()
    {
        using var fixture = RuntimeFixture.Create();
        fixture.Tcp.DisconnectResult = false;
        fixture.Bluetooth.DisconnectResult = true;

        Assert.True(fixture.Runtime.DisconnectActiveClient());
        Assert.Equal(1, fixture.Tcp.DisconnectCallCount);
        Assert.Equal(1, fixture.Bluetooth.DisconnectCallCount);

        await fixture.Runtime.StopAsync();
    }

    private sealed class RuntimeFixture : IDisposable
    {
        private RuntimeFixture(
            AgentRuntime runtime,
            ActiveSessionManager sessions,
            TrackingDisposable input,
            TrackingDisposable clipboard,
            TrackingDisposable shortcutProfiles,
            FakeTransport tcp,
            FakeTransport bluetooth)
        {
            Runtime = runtime;
            Sessions = sessions;
            Input = input;
            Clipboard = clipboard;
            ShortcutProfiles = shortcutProfiles;
            Tcp = tcp;
            Bluetooth = bluetooth;
        }

        public AgentRuntime Runtime { get; }

        public ActiveSessionManager Sessions { get; }

        public TrackingDisposable Input { get; }

        public TrackingDisposable Clipboard { get; }

        public TrackingDisposable ShortcutProfiles { get; }

        public FakeTransport Tcp { get; }

        public FakeTransport Bluetooth { get; }

        public static RuntimeFixture Create(
            Exception? bluetoothStopException = null,
            Exception? tcpStartException = null,
            Exception? bluetoothStartException = null)
        {
            var input = new TrackingDisposable();
            var clipboard = new TrackingDisposable();
            var shortcutProfiles = new TrackingDisposable();
            var sessions = new ActiveSessionManager();
            var tcp = new FakeTransport
            {
                StartException = tcpStartException,
            };
            var bluetooth = new FakeTransport
            {
                StopException = bluetoothStopException,
                StartException = bluetoothStartException,
            };
            var runtime = new AgentRuntime(
                input,
                clipboard,
                shortcutProfiles,
                sessions,
                tcp,
                bluetooth);

            return new RuntimeFixture(
                runtime,
                sessions,
                input,
                clipboard,
                shortcutProfiles,
                tcp,
                bluetooth);
        }

        public void Dispose()
        {
            // Tests explicitly stop the runtime so assertions observe the real
            // shutdown task and its failure behavior.
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        private int _disposeCallCount;

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCallCount);
        }
    }

    private sealed class FakeTransport : IRuntimeTransport
    {
        event Action<ConnectionState>? IRuntimeTransport.ConnectionStateChanged
        {
            add { }
            remove { }
        }

        event Action<string>? IRuntimeTransport.ServerMessage
        {
            add { }
            remove { }
        }

        public Exception? StopException { get; set; }

        public Exception? StartException { get; set; }

        public bool DisconnectResult { get; set; }

        public int StopCallCount => Volatile.Read(ref _stopCallCount);

        public int StartCallCount => Volatile.Read(ref _startCallCount);

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public int DisconnectCallCount => Volatile.Read(ref _disconnectCallCount);

        private int _stopCallCount;
        private int _startCallCount;
        private int _disposeCallCount;
        private int _disconnectCallCount;

        public void Start()
        {
            Interlocked.Increment(ref _startCallCount);
            if (StartException is not null)
            {
                throw StartException;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _stopCallCount);
            return StopException is null
                ? Task.CompletedTask
                : Task.FromException(StopException);
        }

        public bool DisconnectLatestClient()
        {
            Interlocked.Increment(ref _disconnectCallCount);
            return DisconnectResult;
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCallCount);
        }
    }
}
