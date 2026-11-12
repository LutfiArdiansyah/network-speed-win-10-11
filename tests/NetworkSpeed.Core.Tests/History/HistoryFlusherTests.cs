using NetworkSpeed.Core.History;
using NetworkSpeed.Core.Logging;

namespace NetworkSpeed.Core.Tests.History;

public class HistoryFlusherTests
{
    private static readonly Guid A = new(1, 0, 0, new byte[8]);

    private static TrafficBucket<MinuteKey> M(string minute, ulong sent = 1) => new(new MinuteKey(A, minute), sent, 0);

    private static TrafficBucket<DayKey> D(string day, ulong sent = 1) => new(new DayKey(A, day), sent, 0);

    [Fact]
    public void Flush_Success_ClearsPending()
    {
        var store = new FakeStore();
        var flusher = new HistoryFlusher(store, new ListLog());
        flusher.Enqueue([M("2026-11-12T10:00")], [D("2026-11-12")]);
        Assert.True(flusher.TryFlush());
        Assert.Single(store.Minutes);
        Assert.True(flusher.TryFlush());
        Assert.Single(store.Minutes);
    }

    [Fact]
    public void Flush_StoreThrows_KeepsPendingAndRetries()
    {
        var store = new FakeStore { FailuresLeft = 1 };
        var log = new ListLog();
        var flusher = new HistoryFlusher(store, log);
        flusher.Enqueue([M("2026-11-12T10:00", 7)], [D("2026-11-12", 7)]);
        Assert.False(flusher.TryFlush());
        Assert.Contains(log.Warnings, w => w.Contains(nameof(IOException), StringComparison.Ordinal));
        Assert.Empty(store.Minutes);

        flusher.Enqueue([M("2026-11-12T10:01", 3)], []);
        Assert.True(flusher.TryFlush());
        Assert.Equal(["2026-11-12T10:00", "2026-11-12T10:01"], store.Minutes.Select(m => m.Key.LocalMinute));
        Assert.Equal(7UL, Assert.Single(store.Days).SentBytes);
    }

    [Fact]
    public void Flush_PendingCapped_DropsOldest()
    {
        var store = new FakeStore { FailuresLeft = 3 };
        var log = new ListLog();
        var flusher = new HistoryFlusher(store, log, maxPendingMinutes: 2);
        flusher.Enqueue([M("2026-11-12T10:00")], []);
        flusher.TryFlush();
        flusher.Enqueue([M("2026-11-12T10:01")], []);
        flusher.TryFlush();
        flusher.Enqueue([M("2026-11-12T10:02")], []);
        flusher.TryFlush();
        Assert.True(flusher.TryFlush());
        Assert.Equal(["2026-11-12T10:01", "2026-11-12T10:02"], store.Minutes.Select(m => m.Key.LocalMinute));
        Assert.Contains(log.Warnings, w => w.Contains("dropped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Enqueue_WhileFailing_DayTotalsMergedPerKey()
    {
        var store = new FakeStore { FailuresLeft = 1_000 };
        var flusher = new HistoryFlusher(store, new ListLog());
        for (var i = 0; i < 100; i++)
        {
            flusher.Enqueue([], [D("2026-11-12", 2)]);
            flusher.TryFlush();
        }

        Assert.Equal(1, flusher.PendingDayCount);
        store.FailuresLeft = 0;
        Assert.True(flusher.TryFlush());
        Assert.Equal(200UL, Assert.Single(store.Days).SentBytes);
    }

    private sealed class FakeStore : IHistoryStore
    {
        public int FailuresLeft { get; set; }

        public List<TrafficBucket<MinuteKey>> Minutes { get; } = [];

        public List<TrafficBucket<DayKey>> Days { get; } = [];

        public void Write(IReadOnlyList<TrafficBucket<MinuteKey>> minutes, IReadOnlyList<TrafficBucket<DayKey>> days)
        {
            if (FailuresLeft > 0)
            {
                FailuresLeft--;
                throw new IOException("disk full at C:\\secret");
            }

            Minutes.AddRange(minutes);
            Days.AddRange(days);
        }

        public void Purge(string minuteCutoff, string dayCutoff)
        {
        }

        public IReadOnlyList<HistoryPoint> ReadMinutes(IReadOnlyCollection<Guid> adapters, string fromLocalMinute, string toLocalMinute) => [];

        public IReadOnlyList<HistoryPoint> ReadDays(IReadOnlyCollection<Guid> adapters, string fromLocalDay, string toLocalDay) => [];
    }

    private sealed class ListLog : IAppLog
    {
        public List<string> Warnings { get; } = [];

        public void Info(string message)
        {
        }

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) => Warnings.Add(message);
    }
}
