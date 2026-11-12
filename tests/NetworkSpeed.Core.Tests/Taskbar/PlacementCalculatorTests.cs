using NetworkSpeed.Core.Taskbar;

namespace NetworkSpeed.Core.Tests.Taskbar;

public class PlacementCalculatorTests
{
    private const int W = 104;
    private const int H = 34;

    private static Func<OverlayPosition, PixelOffset> NoOffset => _ => default;

    // Angka dari spike: Win11 rata tengah, 1920x1080, taskbar 48 px.
    internal static TaskbarLayout Win11Centered(int? lastAppRight = 1445, int? widgetsRight = null, bool uia = true) => new(
        MonitorId: @"\\.\DISPLAY1",
        IsPrimary: true,
        Taskbar: new PixelRect(0, 1032, 1920, 1080),
        Edge: TaskbarEdge.Bottom,
        IsWindows11: true,
        IsCentered: true,
        HasUiaDetail: uia,
        WidgetsRight: widgetsRight,
        FirstButtonLeft: uia ? 476 : null,
        LastAppRight: uia ? lastAppRight : null,
        TrayLeft: 1679);

    [Fact]
    public void Calculate_Right_PlacesLeftOfTray()
    {
        var p = PlacementCalculator.Calculate(Win11Centered(), OverlayPosition.Right, W, H, NoOffset);
        Assert.Equal((true, 1567, 1039, OverlayPosition.Right, PlacementSource.Requested), (p.IsVisible, p.X, p.Y, p.Effective, p.Source));
    }

    [Fact]
    public void Calculate_Center_PlacesAfterLastApp()
    {
        var p = PlacementCalculator.Calculate(Win11Centered(), OverlayPosition.Center, W, H, NoOffset);
        Assert.Equal((1453, OverlayPosition.Center), (p.X, p.Effective));
    }

    [Fact]
    public void Calculate_Left_NoWidgets_PlacesAtLeftEdge()
    {
        var p = PlacementCalculator.Calculate(Win11Centered(), OverlayPosition.Left, W, H, NoOffset);
        Assert.Equal((8, OverlayPosition.Left), (p.X, p.Effective));
    }

    [Fact]
    public void Calculate_Left_WithWidgets_PlacesAfterWidgets()
    {
        var p = PlacementCalculator.Calculate(Win11Centered(widgetsRight: 150), OverlayPosition.Left, W, H, NoOffset);
        Assert.Equal(158, p.X);
    }

    [Fact]
    public void Calculate_Left_OnWindows10_FallsBackRight()
    {
        var win10 = Win11Centered() with { IsWindows11 = false, IsCentered = false };
        var p = PlacementCalculator.Calculate(win10, OverlayPosition.Left, W, H, NoOffset);
        Assert.Equal((1567, OverlayPosition.Right, PlacementSource.FallbackRight), (p.X, p.Effective, p.Source));
    }

    [Fact]
    public void Calculate_NoRoomAnywhere_HiddenNotOverlapping()
    {
        var p = PlacementCalculator.Calculate(Win11Centered(lastAppRight: 1600), OverlayPosition.Center, W, H, NoOffset);
        Assert.False(p.IsVisible);
        Assert.Equal(PlacementSource.Hidden, p.Source);
        Assert.Contains("space", p.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Calculate_Win32OnlyLayout_RightStillWorks()
    {
        var p = PlacementCalculator.Calculate(Win11Centered(uia: false), OverlayPosition.Right, W, H, NoOffset);
        Assert.Equal((true, 1567), (p.IsVisible, p.X));
    }

    [Fact]
    public void Calculate_Win32OnlyLayout_CenterFallsBackRight()
    {
        var p = PlacementCalculator.Calculate(Win11Centered(uia: false), OverlayPosition.Center, W, H, NoOffset);
        Assert.Equal((PlacementSource.FallbackRight, 1567), (p.Source, p.X));
    }

    [Fact]
    public void Calculate_VerticalTaskbar_Hidden()
    {
        var vertical = Win11Centered() with { Taskbar = new PixelRect(0, 0, 62, 1080), Edge = TaskbarEdge.Left };
        var p = PlacementCalculator.Calculate(vertical, OverlayPosition.Right, W, H, NoOffset);
        Assert.False(p.IsVisible);
        Assert.Contains("Vertical", p.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_NullLayout_IsHiddenWithReason()
    {
        var p = PlacementCalculator.Calculate(null, OverlayPosition.Right, W, H, NoOffset);
        Assert.False(p.IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(p.Reason));
    }

    [Fact]
    public void Calculate_Offset_AppliedAndClampedInsideTaskbar()
    {
        var shifted = PlacementCalculator.Calculate(Win11Centered(), OverlayPosition.Right, W, H, _ => new PixelOffset(10, -2));
        Assert.Equal((1577, 1037), (shifted.X, shifted.Y));
        var clamped = PlacementCalculator.Calculate(Win11Centered(), OverlayPosition.Right, W, H, _ => new PixelOffset(500, 200));
        Assert.Equal((1920 - W, 1080 - H), (clamped.X, clamped.Y));
    }

    [Fact]
    public void Calculate_Fallback_UsesOffsetOfEffectivePosition()
    {
        var win10 = Win11Centered() with { IsWindows11 = false, IsCentered = false };
        var p = PlacementCalculator.Calculate(
            win10,
            OverlayPosition.Left,
            W,
            H,
            pos => pos == OverlayPosition.Right ? new PixelOffset(-5, 0) : new PixelOffset(99, 0));
        Assert.Equal(1562, p.X);
    }

    [Fact]
    public void Calculate_TopTaskbar_CentersVertically()
    {
        var top = Win11Centered() with { Taskbar = new PixelRect(0, 0, 1920, 40), Edge = TaskbarEdge.Top };
        Assert.Equal(3, PlacementCalculator.Calculate(top, OverlayPosition.Right, W, H, NoOffset).Y);
    }
}
