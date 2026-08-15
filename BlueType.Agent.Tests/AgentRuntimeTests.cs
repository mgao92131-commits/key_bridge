using BlueType.Agent.Application.Sessions;
using BlueType.Agent.Host;
using BlueType.Agent.Models;
using BlueType.Agent.Transport;

namespace BlueType.Agent.Tests;

public sealed class AgentRuntimeTests
{
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

        public static RuntimeFixture Create(Exception? bluetoothStopException = null)
        {
            var input = new TrackingDisposable();
            var clipboard = new TrackingDisposable();
            var shortcutProfiles = new TrackingDisposable();
            var sessions = new ActiveSessionManager();
            var tcp = new FakeTransport();
            var bluetooth = new FakeTransport
            {
                StopException = bluetoothStopException,
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

        public bool DisconnectResult { get; set; }

        public int StopCallCount => Volatile.Read(ref _stopCallCount);

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public int DisconnectCallCount => Volatile.Read(ref _disconnectCallCount);

        private int _stopCallCount;
        private int _disposeCallCount;
        private int _disconnectCallCount;

        public void Start()
        {
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
