using NetworkSpeed.Core.Display;
using NetworkSpeed.Core.History;

namespace NetworkSpeed.Core.Tests.Display;

public class HistorySeriesTests
{
    private static readonly DateTime From = new(2026, 11, 12, 10, 0, 0);

    [Fact]
    public void PerMinute_FillsGapsWithZero()
    {
        var series = HistorySeries.PerMinute([new HistoryPoint("2026-11-12T10:00", 1, 2), new HistoryPoint("2026-11-12T10:03", 3, 4)], From, From.AddMinutes(3));
        Assert.Equal([1UL, 0, 0, 3], series.Select(p => p.SentBytes));
        Assert.Equal(From.AddMinutes(2), series[2].LocalTime);
    }

    [Fact]
    public void PerMinute_IgnoresMalformedAndOutOfRange()
    {
        var series = HistorySeries.PerMinute([new HistoryPoint("garbage", 9, 9), new HistoryPoint("2026-11-12T11:30", 9, 9)], From, From.AddMinutes(1));
        Assert.All(series, p => Assert.Equal(0UL, p.SentBytes));
        Assert.Equal(2, series.Count);
    }

    [Fact]
    public void PerHour_SumsMinutesIntoHours()
    {
        var series = HistorySeries.PerHour(
            [new HistoryPoint("2026-11-12T10:05", 1, 10), new HistoryPoint("2026-11-12T10:59", 2, 20), new HistoryPoint("2026-11-12T12:00", 5, 50)],
            From,
            From.AddHours(23));
        Assert.Equal(24, series.Count);
        Assert.Equal((3UL, 30UL), (series[0].SentBytes, series[0].ReceivedBytes));
        Assert.Equal(5UL, series[2].SentBytes);
    }

    [Fact]
    public void PerHour_SevenDays_Is168Points()
        => Assert.Equal(168, HistorySeries.PerHour([], From, From.AddDays(7).AddHours(-1)).Count);

    [Fact]
    public void Live_KeepsLast60()
    {
        var buffer = new LiveHistoryBuffer();
        for (var i = 0; i < 75; i++)
        {
            buffer.Add(i, i * 2);
        }

        var snapshot = buffer.Snapshot();
        Assert.Equal(60, snapshot.Count);
        Assert.Equal((15d, 30d), snapshot[0]);
        Assert.Equal((74d, 148d), snapshot[^1]);
    }

    [Theory]
    [InlineData(0d, 1d)]
    [InlineData(-5d, 1d)]
    [InlineData(0.3, 0.5)]
    [InlineData(7d, 10d)]
    [InlineData(1234d, 2000d)]
    [InlineData(5000d, 5000d)]
    [InlineData(1e12, 1e12)]
    public void NiceMax_Cases(double max, double expected) => Assert.Equal(expected, ChartScale.NiceMax(max), 9);
}
