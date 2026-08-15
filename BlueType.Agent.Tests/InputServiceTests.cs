using BlueType.Agent.Infrastructure.Input;

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
}
