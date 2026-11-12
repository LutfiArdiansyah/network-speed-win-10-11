using NetworkSpeed.Core.Storage;
using NetworkSpeed.Core.Tests.Fakes;

namespace NetworkSpeed.Core.Tests.Storage;

public sealed class NotificationStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-state-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTime _time = new(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero));

    public NotificationStateStoreTests() => Directory.CreateDirectory(_dir);

    private string StatePath => Path.Combine(_dir, "state.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Store_Missing_Empty() => Assert.Empty(new NotificationStateStore(StatePath, _time).Load());

    [Fact]
    public void Store_RoundTrip_SurvivesRestart()
    {
        new NotificationStateStore(StatePath, _time).MarkSent(["daily:2026-11-12:5:80"]);
        Assert.Contains("daily:2026-11-12:5:80", new NotificationStateStore(StatePath, _time).Load());
    }

    [Fact]
    public void Store_PrunesOlderThan62Days()
    {
        var store = new NotificationStateStore(StatePath, _time);
        store.MarkSent(["old"]);
        _time.Advance(TimeSpan.FromDays(63));
        store.MarkSent(["new"]);
        Assert.Equal(["new"], new NotificationStateStore(StatePath, _time).Load());
    }

    [Fact]
    public void Store_LockedFile_DoesNotCacheEmptyState()
    {
        new NotificationStateStore(StatePath, _time).MarkSent(["daily:2026-11-12:5:80"]);
        var store = new NotificationStateStore(StatePath, _time);
        using (new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // File terkunci (antivirus, sinkronisasi): baca gagal, tetapi tidak boleh dianggap "belum ada yang terkirim" selamanya.
            Assert.Throws<IOException>(() => store.Load());
        }

        Assert.Contains("daily:2026-11-12:5:80", store.Load());
    }

    [Fact]
    public void Store_CorruptFile_StartsEmpty()
    {
        File.WriteAllText(StatePath, "{ not json");
        var store = new NotificationStateStore(StatePath, _time);
        Assert.Empty(store.Load());
        Assert.Single(Directory.GetFiles(_dir, "state.json.corrupt-*"));
        store.MarkSent(["a"]);
        Assert.Contains("a", store.Load());
    }
}
