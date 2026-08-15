using BlueType.Agent.Infrastructure.Input;
using BlueType.Agent.Native;

namespace BlueType.Agent.Tests;

public sealed class InputServiceTests
{
    [Fact]
    public async Task SendTextAsync_EmptyText_CompletesWithoutInjection()
    {
        using var input = new InputInjector();

        await input.SendTextAsync(string.Empty);
    }

    [Fact]
    public async Task MoveMouseAsync_ZeroDelta_CompletesWithoutInjection()
    {
        using var input = new InputInjector();

        await input.MoveMouseAsync(0, 0);
    }

    [Fact]
    public async Task ClickMouseAsync_NonPositiveRepeat_CompletesWithoutInjection()
    {
        using var input = new InputInjector();

        await input.ClickMouseAsync("LEFT", 0);
        await input.ClickMouseAsync("LEFT", -1);
    }

    [Fact]
    public async Task ReleaseKeyAsync_UnpressedKnownKey_CompletesWithoutInjection()
    {
        using var input = new InputInjector();

        await input.ReleaseKeyAsync("A");
    }

    [Fact]
    public async Task ReleaseAllAsync_WhenNothingIsPressed_CompletesWithoutInjection()
    {
        using var input = new InputInjector();

        await input.ReleaseAllKeysAsync();
        await input.ReleaseAllMouseButtonsAsync();
    }

    [Fact]
    public async Task CommandWithCancelledToken_ThrowsBeforeItIsQueued()
    {
        using var input = new InputInjector();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => input.PressKeyAsync("A", cancellation.Token));
    }

    [Fact]
    public void KeyboardInjector_DoesNotSendDuplicateKeyDown_AndReleaseClearsState()
    {
        var sender = new RecordingInputSender();
        var keyboard = new WindowsKeyboardInjector(sender);

        keyboard.PressKey("A");
        keyboard.PressKey("A");
        keyboard.ReleaseAll();
        keyboard.ReleaseAll();

        Assert.Equal(2, sender.Batches.Count);
        Assert.Single(sender.Batches[0]);
        Assert.Single(sender.Batches[1]);
    }

    [Fact]
    public void KeyboardInjector_IgnoresReleaseForUnpressedKnownKey()
    {
        var sender = new RecordingInputSender();
        var keyboard = new WindowsKeyboardInjector(sender);

        keyboard.ReleaseKey("A");

        Assert.Empty(sender.Batches);
    }

    [Fact]
    public void MouseInjector_DoesNotSendDuplicateButtonDown_AndReleaseClearsState()
    {
        var sender = new RecordingInputSender();
        var mouse = new WindowsMouseInjector(sender);

        mouse.SetButtonState("LEFT", isDown: true);
        mouse.SetButtonState("LEFT", isDown: true);
        mouse.ReleaseAll();
        mouse.ReleaseAll();

        Assert.Equal(2, sender.Batches.Count);
        Assert.Single(sender.Batches[0]);
        Assert.Single(sender.Batches[1]);
    }

    private sealed class RecordingInputSender : IWindowsInputSender
    {
        public List<IReadOnlyList<Win32.INPUT>> Batches { get; } = [];

        public void Send(IReadOnlyList<Win32.INPUT> inputs)
        {
            Batches.Add(inputs.ToArray());
        }
    }
}
