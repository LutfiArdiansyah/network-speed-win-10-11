using NetworkSpeed.Core.Taskbar;

namespace NetworkSpeed.Core.Tests.Taskbar;

public class PositionSupportEvaluatorTests
{
    [Fact]
    public void Evaluate_Win11Centered_AllSupported()
    {
        var result = PositionSupportEvaluator.Evaluate(PlacementCalculatorTests.Win11Centered());
        Assert.Equal(3, result.Count);
        Assert.All(result, s => Assert.True(s.IsSupported));
    }

    [Fact]
    public void Evaluate_Win11LeftAligned_LeftUnsupportedWithReason()
    {
        var leftAligned = PlacementCalculatorTests.Win11Centered() with { IsCentered = false };
        var left = PositionSupportEvaluator.Evaluate(leftAligned).Single(s => s.Position == OverlayPosition.Left);
        Assert.False(left.IsSupported);
        Assert.Contains("Start", left.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_Vertical_AllUnsupported()
    {
        var vertical = PlacementCalculatorTests.Win11Centered() with { Edge = TaskbarEdge.Right, Taskbar = new PixelRect(1858, 0, 1920, 1080) };
        Assert.All(PositionSupportEvaluator.Evaluate(vertical), s => Assert.False(s.IsSupported));
    }

    [Fact]
    public void Evaluate_Win32Only_CenterUnsupported_RightSupported()
    {
        var result = PositionSupportEvaluator.Evaluate(PlacementCalculatorTests.Win11Centered(uia: false));
        Assert.False(result.Single(s => s.Position == OverlayPosition.Center).IsSupported);
        Assert.True(result.Single(s => s.Position == OverlayPosition.Right).IsSupported);
    }
}
