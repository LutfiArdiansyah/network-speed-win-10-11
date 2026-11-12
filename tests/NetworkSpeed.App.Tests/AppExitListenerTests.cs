using NetworkSpeed.App.Features;

namespace NetworkSpeed.App.Tests;

public class AppExitListenerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static string NewName() => @"Local\NetworkSpeed.Test." + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Listener_EventSet_CallsOnExitOnce()
    {
        var name = NewName();
        using var signal = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        var calls = 0;
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new AppExitListener(() => EventWaitHandle.OpenExisting(name), () =>
        {
            Interlocked.Increment(ref calls);
            fired.TrySetResult();
        });

        Assert.True(listener.TryAttach());
        Assert.True(listener.TryAttach()); // dipanggil lagi saat status service berubah: tidak mendaftar dua kali.
        signal.Set();
        await fired.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Listener_OpenReturnsNull_ThenAvailable_AttachesLater()
    {
        var name = NewName();
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new AppExitListener(
            () => EventWaitHandle.TryOpenExisting(name, out var handle) ? handle : null,
            () => fired.TrySetResult());

        Assert.False(listener.TryAttach()); // service belum jalan.
        using var signal = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        Assert.True(listener.TryAttach());
        signal.Set();
        await fired.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Listener_Dispose_NoCallback()
    {
        var name = NewName();
        using var signal = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        var calls = 0;
        var listener = new AppExitListener(() => EventWaitHandle.OpenExisting(name), () => Interlocked.Increment(ref calls));
        Assert.True(listener.TryAttach());
        listener.Dispose();
        signal.Set();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Equal(0, calls);
        Assert.False(listener.TryAttach());
    }

    [Fact]
    public void Listener_OpenThrows_ReturnsFalse()
    {
        using var listener = new AppExitListener(() => throw new UnauthorizedAccessException(), () => { });
        Assert.False(listener.TryAttach());
    }
}
