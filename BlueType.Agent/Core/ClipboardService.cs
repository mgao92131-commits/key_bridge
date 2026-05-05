using System.Threading.Channels;

namespace BlueType.Agent.Core;

internal sealed class ClipboardService : IDisposable
{
    private readonly Channel<ClipboardRequest> _requests;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Thread _thread;

    public ClipboardService()
    {
        _requests = Channel.CreateUnbounded<ClipboardRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        _thread = new Thread(RunClipboardLoop)
        {
            IsBackground = true,
            Name = "BlueType.Clipboard",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(
            () =>
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    Clipboard.SetText(text);
                }

                return string.Empty;
            },
            cancellationToken);
    }

    public Task<string> GetTextAsync(CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(
            () => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty,
            cancellationToken);
    }

    public void Dispose()
    {
        _requests.Writer.TryComplete();
        _shutdown.Cancel();
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
        _shutdown.Dispose();
    }

    private Task<string> EnqueueAsync(Func<string> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_requests.Writer.TryWrite(new ClipboardRequest(action, completion)))
        {
            throw new InvalidOperationException("Clipboard queue is unavailable.");
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        }

        return completion.Task;
    }

    private void RunClipboardLoop()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                if (!_requests.Reader.WaitToReadAsync(_shutdown.Token).AsTask().GetAwaiter().GetResult())
                {
                    break;
                }

                while (_requests.Reader.TryRead(out var request))
                {
                    try
                    {
                        var result = request.Action();
                        request.Completion.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        request.Completion.TrySetException(ex);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private sealed record ClipboardRequest(Func<string> Action, TaskCompletionSource<string> Completion);
}
