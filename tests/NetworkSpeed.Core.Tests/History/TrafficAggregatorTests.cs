using NetworkSpeed.Core.History;
using NetworkSpeed.Core.Sampling;

namespace NetworkSpeed.Core.Tests.History;

public class TrafficAggregatorTests
{
    private static readonly Guid A = new(1, 0, 0, new byte[8]);
    private static readonly Guid B = new(2, 0, 0, new byte[8]);

    private static DateTime T(int day, int hour, int minute, int second = 0) => new(2026, 11, day, hour, minute, second);

    [Fact]
    public void Aggregate_SameMinute_Sums()
    {
        var agg = new TrafficAggregator();
        agg.Add([new ByteDelta(A, 10, 100)], T(12, 10, 15, 1));
        agg.Add([new ByteDelta(A, 5, 50)], T(12, 10, 15, 30));
        var bucket = Assert.Single(agg.DrainMinutesBefore(T(12, 10, 16)));
        Assert.Equal(new TrafficBucket<MinuteKey>(new MinuteKey(A, "2026-11-12T10:15"), 15, 150), bucket);
    }

    [Fact]
    public void Aggregate_TwoAdapters_SeparateBuckets()
    {
        var agg = new TrafficAggregator();
        agg.Add([new ByteDelta(A, 1, 1), new ByteDelta(B, 2, 2)], T(12, 10, 15));
        Assert.Equal(2, agg.DrainMinutesBefore(T(12, 10, 16)).Count);
    }

    [Fact]
    public void Aggregate_ZeroDelta_NotStored()
    {
        var agg = new TrafficAggregator();
        agg.Add([new ByteDelta(A, 0, 0)], T(12, 10, 15));
        Assert.Empty(agg.DrainMinutesBefore(T(12, 10, 16)));
        Assert.Empty(agg.DrainDays());
    }

    [Fact]
    public void Drain_ExcludesCurrentMinute()
    {
        var agg = new TrafficAggregator();
        agg.Add([new ByteDelta(A, 1, 1)], T(12, 10, 15));
        agg.Add([new ByteDelta(A, 2, 2)], T(12, 10, 16, 5));
        var drained = Assert.Single(agg.DrainMinutesBefore(T(12, 10, 16, 30)));
        Assert.Equal("2026-11-12T10:15", drained.Key.LocalMinute);
        Assert.Single(agg.DrainAllMinutes());
    }

    [Fact]
    public void Aggregate_RepeatedLocalMinute_SumsIntoSameRow()
    {
        // DST mundur: 02:30 terjadi dua kali; keduanya masuk satu bucket (spec E-15).
        var agg = new TrafficAggregator();
        agg.Add([new ByteDelta(A, 100, 0)], T(1, 2, 30, 10));
        agg.Add([new ByteDelta(A, 50, 0)], T(1, 2, 30, 20));
        var bucket = Assert.Single(agg.DrainAllMinutes());
        Assert.Equal(150UL, bucket.SentBytes);
    }

    [Fact]
    public void Drain_AfterClockGoesBack_StillFlushesOlderLookingMinute()
    {
        // Setelah jam mundur, menit "02:59" di memori tidak boleh tertahan sampai jam 02:59 berikutnya.
        var agg = new TrafficAggregator();
        agg.Add([new ByteDelta(A, 1, 0)], T(1, 2, 59, 50));
        Assert.Single(agg.DrainMinutesBefore(T(1, 2, 0, 5)));
    }

    [Fact]
    public void Days_AccumulateAcrossMinutes_AndDrainIsIncremental()
    {
        var agg = new TrafficAggregator();
        agg.Add([new ByteDelta(A, 10, 1)], T(12, 10, 15));
        agg.Add([new ByteDelta(A, 20, 2)], T(12, 11, 0));
        var day = Assert.Single(agg.DrainDays());
        Assert.Equal(new TrafficBucket<DayKey>(new DayKey(A, "2026-11-12"), 30, 3), day);
        Assert.Empty(agg.DrainDays());
        agg.Add([new ByteDelta(A, 5, 5)], T(12, 12, 0));
        Assert.Equal(5UL, Assert.Single(agg.DrainDays()).SentBytes);
    }

    [Fact]
    public void Aggregate_Midnight_SplitsDays()
    {
        var agg = new TrafficAggregator();
        agg.Add([new ByteDelta(A, 1, 1)], T(12, 23, 59, 59));
        agg.Add([new ByteDelta(A, 2, 2)], T(13, 0, 0, 1));
        Assert.Equal(["2026-11-12", "2026-11-13"], agg.DrainDays().Select(d => d.Key.LocalDay).Order());
    }

    [Fact]
    public void SnapshotMinutes_DoesNotDrain()
    {
        var agg = new TrafficAggregator();
        agg.Add([new ByteDelta(A, 1, 1)], T(12, 10, 15));
        Assert.Single(agg.SnapshotMinutes());
        Assert.Single(agg.DrainAllMinutes());
    }

    [Fact]
    public void Retention_Cutoffs_SevenAndSixtyTwoDays()
    {
        var now = new DateTime(2026, 11, 12, 10, 15, 42);
        Assert.Equal("2026-11-05T10:15", RetentionPolicy.MinuteCutoff(now));
        Assert.Equal("2026-09-11", RetentionPolicy.DayCutoff(now));
    }
}
