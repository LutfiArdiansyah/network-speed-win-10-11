using NetworkSpeed.Core.Display;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Core.Settings;

namespace NetworkSpeed.Core.Tests.Display;

public class OverlayTextBuilderTests
{
    private const double MB = 1024d * 1024;

    private static SpeedReading Reading(double up, double down, bool offline = false) => new(up, down, offline, [], 0, 0);

    [Fact]
    public void Build_TwoLines_FormatsBothDirections()
    {
        var text = OverlayTextBuilder.Build(Reading(1.2 * MB, 5.43 * MB), new UserSettings(), null);
        Assert.Equal(["↑ 1.2 MB/s", "↓ 5.4 MB/s"], text.Lines);
    }

    [Fact]
    public void Build_OneLine_SingleLine()
    {
        var text = OverlayTextBuilder.Build(Reading(1.2 * MB, 5.43 * MB), new UserSettings { Layout = OverlayLayout.OneLine }, null);
        Assert.Equal(["↑ 1.2 MB/s  ↓ 5.4 MB/s"], text.Lines);
    }

    [Fact]
    public void Build_NullReading_ShowsZero()
        => Assert.Equal(["↑ 0.0 KB/s", "↓ 0.0 KB/s"], OverlayTextBuilder.Build(null, new UserSettings(), null).Lines);

    [Fact]
    public void Build_Offline_ShowsOffline()
    {
        Assert.Equal(["Offline"], OverlayTextBuilder.Build(Reading(0, 0, offline: true), new UserSettings(), null).Lines);
        Assert.Equal(["Offline"], OverlayTextBuilder.Build(Reading(0, 0, offline: true), new UserSettings { Layout = OverlayLayout.OneLine }, null).Lines);
    }

    [Fact]
    public void Build_AboveThreshold_SetsAlertPerDirection()
    {
        var text = OverlayTextBuilder.Build(Reading(2 * MB, 10 * 1024), new UserSettings { AlertThresholdBytesPerSecond = MB }, null);
        Assert.True(text.IsUpAlert);
        Assert.False(text.IsDownAlert);
    }

    [Fact]
    public void Build_NoThreshold_NoAlert()
    {
        var text = OverlayTextBuilder.Build(Reading(999 * MB, 999 * MB), new UserSettings(), null);
        Assert.False(text.IsUpAlert || text.IsDownAlert);
    }

    [Fact]
    public void Build_LatencyOnlyInOneLine()
    {
        var latency = new LatencyReading(TimeSpan.FromMilliseconds(12.4));
        var oneLine = OverlayTextBuilder.Build(Reading(0, 0), new UserSettings { Layout = OverlayLayout.OneLine, ShowLatencyOnOverlay = true }, latency);
        Assert.Equal(["↑ 0.0 KB/s  ↓ 0.0 KB/s  12 ms"], oneLine.Lines);
        var twoLines = OverlayTextBuilder.Build(Reading(0, 0), new UserSettings { ShowLatencyOnOverlay = true }, latency);
        Assert.Equal(["↑ 0.0 KB/s", "↓ 0.0 KB/s"], twoLines.Lines);
    }

    [Theory]
    [InlineData(12.4, "12 ms")]
    [InlineData(0.3, "<1 ms")]
    [InlineData(999.6, "1000 ms")]
    public void LatencyFormatter_RoundTrip(double ms, string expected)
        => Assert.Equal(expected, LatencyFormatter.Format(new LatencyReading(TimeSpan.FromMilliseconds(ms))));

    [Fact]
    public void LatencyFormatter_Timeout()
        => Assert.Equal("timeout", LatencyFormatter.Format(new LatencyReading(null)));

    [Fact]
    public void LatencyFormatter_NoReading()
        => Assert.Equal("—", LatencyFormatter.Format(null));
}
