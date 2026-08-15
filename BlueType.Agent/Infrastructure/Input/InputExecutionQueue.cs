using System.Threading.Channels;

namespace BlueType.Agent.Infrastructure.Input;

internal sealed class InputExecutionQueue : IDisposable
{
    private readonly Channel<QueuedInjection> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;

    public InputExecutionQueue()
    {
        _queue = Channel.CreateUnbounded<QueuedInjection>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _worker = Task.Run(ProcessQueueAsync);
    }

    public Task EnqueueAsync(Action action, CancellationToken cancellationToken = default)
    {
        return EnqueueCoreAsync(action, cancellationToken);
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            _worker.GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore worker shutdown exceptions during app exit.
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private async Task EnqueueCoreAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _queue.Writer.WriteAsync(new QueuedInjection(action, completion), cancellationToken);

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        await completion.Task;
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var queued in _queue.Reader.ReadAllAsync(_shutdown.Token))
        {
            try
            {
                queued.Action();
                queued.Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                queued.Completion.TrySetException(ex);
            }
        }
    }

    private sealed record QueuedInjection(Action Action, TaskCompletionSource Completion);
}
