using System.Diagnostics;
using NetworkSpeed.Interop.Taskbar;

namespace NetworkSpeed.Interop.Tests.Taskbar;

// Integration test: bergantung pada taskbar nyata, tanpa asumsi jumlah app atau monitor.
[Collection(UiaTestGroup.Name)]
public class TaskbarLayoutReaderTests(ITestOutputHelper output)
{
    [Fact]
    public void ReadAll_HasExactlyOnePrimary()
        => Assert.Single(new TaskbarLayoutReader().ReadAll(), l => l.IsPrimary);

    [Fact]
    public void ReadAll_TrayLeftInsidePrimaryTaskbar()
    {
        var primary = new TaskbarLayoutReader().ReadAll().Single(l => l.IsPrimary);
        Assert.InRange(primary.TrayLeft, primary.Taskbar.Left, primary.Taskbar.Right);
        Assert.False(string.IsNullOrEmpty(primary.MonitorId));
    }

    [Fact]
    public void ReadAll_HorizontalPrimary_HasUiaDetail()
    {
        var primary = new TaskbarLayoutReader().ReadAll().Single(l => l.IsPrimary);
        Assert.SkipWhen(primary.IsVertical, "Taskbar vertikal: detail UIA tidak dipakai.");
        Assert.True(primary.HasUiaDetail);
        Assert.NotNull(primary.FirstButtonLeft);
        Assert.InRange(primary.FirstButtonLeft!.Value, primary.Taskbar.Left, primary.TrayLeft);
    }

    [Fact]
    public void ReadWin32Rects_MatchesReadAllTaskbarRects()
    {
        var reader = new TaskbarLayoutReader();
        var fromLayouts = reader.ReadAll().Select(l => l.Taskbar).ToHashSet();
        // Rect Win32 memuat semua taskbar (ditambah area tray) untuk deteksi perubahan murah (T-05).
        Assert.Superset(fromLayouts, reader.ReadWin32Rects().ToHashSet());
    }

    [Fact]
    public void ReadAll_Timing()
    {
        var reader = new TaskbarLayoutReader();
        reader.ReadAll();
        var sw = Stopwatch.StartNew();
        const int runs = 20;
        for (var i = 0; i < runs; i++)
        {
            reader.ReadAll();
        }

        output.WriteLine($"ReadAll average: {sw.Elapsed.TotalMilliseconds / runs:F1} ms");
    }
}
