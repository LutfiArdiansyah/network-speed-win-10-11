using NetworkSpeed.Core.Taskbar;

namespace NetworkSpeed.Core.Tests.Taskbar;

public class MonitorSelectorTests
{
    private static readonly TaskbarLayout Primary = PlacementCalculatorTests.Win11Centered();
    private static readonly TaskbarLayout Second = Primary with { MonitorId = @"\\.\DISPLAY2", IsPrimary = false };

    [Fact]
    public void SelectMonitors_PrimaryOnly()
        => Assert.Equal([Primary], MonitorSelector.Select([Second, Primary], MonitorMode.PrimaryOnly, null));

    [Fact]
    public void SelectMonitors_All_ReturnsEvery()
        => Assert.Equal(2, MonitorSelector.Select([Primary, Second], MonitorMode.All, null).Count);

    [Fact]
    public void SelectMonitors_Specific_Found()
        => Assert.Equal([Second], MonitorSelector.Select([Primary, Second], MonitorMode.Specific, Second.MonitorId));

    [Fact]
    public void SelectMonitors_SpecificMissing_FallsBackToPrimary()
        => Assert.Equal([Primary], MonitorSelector.Select([Primary, Second], MonitorMode.Specific, @"\\.\DISPLAY9"));

    [Fact]
    public void SelectMonitors_NoPrimary_ReturnsFirst()
        => Assert.Equal([Second], MonitorSelector.Select([Second], MonitorMode.PrimaryOnly, null));

    [Fact]
    public void SelectMonitors_Empty_ReturnsEmpty()
        => Assert.Empty(MonitorSelector.Select([], MonitorMode.All, null));
}
