using BlueType.Agent.Application.Ports;

namespace BlueType.Agent.Infrastructure.Input;

internal interface IInputRelease
{
    Task ReleaseAllKeysAsync(CancellationToken cancellationToken = default);

    Task ReleaseAllMouseButtonsAsync(CancellationToken cancellationToken = default);
}

internal sealed class WindowsInputService : IDisposable, IInputService, IInputRelease
{
    private readonly InputExecutionQueue _executionQueue;
    private readonly WindowsKeyboardInjector _keyboard;
    private readonly WindowsMouseInjector _mouse;

    public WindowsInputService()
    {
        _executionQueue = new InputExecutionQueue();
        var sender = new WindowsInputSender();
        _keyboard = new WindowsKeyboardInjector(sender);
        _mouse = new WindowsMouseInjector(sender);
    }

    public Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() => _keyboard.SendText(text), cancellationToken);
    }

    public Task TapKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => _keyboard.TapKey(key), cancellationToken);
    }

    public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => _keyboard.PressKey(key), cancellationToken);
    }

    public Task ReleaseKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => _keyboard.ReleaseKey(key), cancellationToken);
    }

    public Task ReleaseAllKeysAsync(CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(_keyboard.ReleaseAll, cancellationToken);
    }

    public Task SendComboAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() => _keyboard.SendCombo(keys), cancellationToken);
    }

    public Task MoveMouseAsync(int dx, int dy, CancellationToken cancellationToken = default)
    {
        if (dx == 0 && dy == 0)
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() => _mouse.Move(dx, dy), cancellationToken);
    }

    public Task ClickMouseAsync(string button, int repeat, CancellationToken cancellationToken = default)
    {
        if (repeat <= 0)
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() => _mouse.Click(button, repeat), cancellationToken);
    }

    public Task PressMouseAsync(string button, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => _mouse.SetButtonState(button, isDown: true), cancellationToken);
    }

    public Task ReleaseMouseAsync(string button, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(() => _mouse.SetButtonState(button, isDown: false), cancellationToken);
    }

    public Task ReleaseAllMouseButtonsAsync(CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(_mouse.ReleaseAll, cancellationToken);
    }

    public Task ScrollMouseAsync(int deltaX, int deltaY, CancellationToken cancellationToken = default)
    {
        if (deltaX == 0 && deltaY == 0)
        {
            return Task.CompletedTask;
        }

        return EnqueueAsync(() => _mouse.Scroll(deltaX, deltaY), cancellationToken);
    }

    public void Dispose()
    {
        try
        {
            ReleaseAllKeysAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort only during shutdown.
        }

        try
        {
            ReleaseAllMouseButtonsAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort only during shutdown.
        }

        _executionQueue.Dispose();
    }

    private async Task EnqueueAsync(Action action, CancellationToken cancellationToken)
    {
        await _executionQueue.EnqueueAsync(action, cancellationToken);
    }

}
