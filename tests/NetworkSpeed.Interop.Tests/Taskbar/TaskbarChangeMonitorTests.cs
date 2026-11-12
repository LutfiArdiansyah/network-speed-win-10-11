using NetworkSpeed.Core.Logging;
using NetworkSpeed.Core.Taskbar;
using NetworkSpeed.Interop.Taskbar;

namespace NetworkSpeed.Interop.Tests.Taskbar;

[Collection(UiaTestGroup.Name)]
public class TaskbarChangeMonitorTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task Poll_TaskbarHandleChanged_RaisesRecreated()
    {
        var reader = new FakeReader { Handle = 100 };
        using var monitor = new TaskbarChangeMonitor(reader, new NullLog(), Poll);
        var recreated = new TaskCompletionSource();
        monitor.Changed += reason =>
        {
            if (reason == TaskbarChangeMonitor.RecreatedReason)
            {
                recreated.TrySetResult();
            }
        };
        monitor.Start();

        reader.Handle = 0;   // Explorer mati
        await Task.Delay(200, TestContext.Current.CancellationToken);
        reader.Handle = 200; // Explorer hidup lagi dengan taskbar baru

        await recreated.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Poll_SameHandle_NoRecreatedEvent()
    {
        var reader = new FakeReader { Handle = 100 };
        using var monitor = new TaskbarChangeMonitor(reader, new NullLog(), Poll);
        var raised = false;
        monitor.Changed += reason => raised |= reason == TaskbarChangeMonitor.RecreatedReason;
        monitor.Start();
        await Task.Delay(400, TestContext.Current.CancellationToken);
        Assert.False(raised);
    }

    private sealed class FakeReader : ITaskbarLayoutReader
    {
        public nint Handle { get; set; }

        public IReadOnlyList<TaskbarLayout> ReadAll() => [];

        public IReadOnlyList<PixelRect> ReadWin32Rects() => [];

        public nint PrimaryTaskbarHandle() => Handle;
    }

    private sealed class NullLog : IAppLog
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
