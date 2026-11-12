using NetworkSpeed.Core.Settings;
using NetworkSpeed.Core.Taskbar;
using NetworkSpeed.Core.Tests.Taskbar;

namespace NetworkSpeed.Core.Tests.Settings;

public class SettingsEditorTests
{
    [Fact]
    public void PositionOptions_Win11Centered_AllEnabled()
    {
        var options = SettingsEditor.PositionOptions([PlacementCalculatorTests.Win11Centered()]);
        Assert.Equal(3, options.Count);
        Assert.All(options, o => Assert.True(o.IsEnabled));
    }

    [Fact]
    public void PositionOptions_MixedTaskbars_LeftDisabledWithReason()
    {
        var centered = PlacementCalculatorTests.Win11Centered();
        var leftAligned = centered with { MonitorId = @"\\.\DISPLAY2", IsPrimary = false, IsCentered = false };
        var left = SettingsEditor.PositionOptions([centered, leftAligned]).Single(o => o.Position == OverlayPosition.Left);
        Assert.False(left.IsEnabled);
        Assert.Equal(PositionSupportEvaluator.LeftCornerReason, left.DisabledReason);
    }

    [Fact]
    public void PositionOptions_NoTaskbar_AllEnabled()
        => Assert.All(SettingsEditor.PositionOptions([]), o => Assert.True(o.IsEnabled));

    [Fact]
    public void LatencyOnOverlayAvailable_OnlyInOneLine()
    {
        Assert.True(SettingsEditor.IsLatencyOnOverlayAvailable(new UserSettings { Layout = OverlayLayout.OneLine }));
        Assert.False(SettingsEditor.IsLatencyOnOverlayAvailable(new UserSettings { Layout = OverlayLayout.TwoLines }));
    }
}
