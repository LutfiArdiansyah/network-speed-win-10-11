using Microsoft.Data.Sqlite;
using NetworkSpeed.Core.History;
using NetworkSpeed.Service.Storage;

namespace NetworkSpeed.Service.Tests.Storage;

public sealed class SqliteHistoryStoreTests : IDisposable
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-db-" + Guid.NewGuid().ToString("N"));

    public SqliteHistoryStoreTests() => Directory.CreateDirectory(_dir);

    private string DbPath => Path.Combine(_dir, "history.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private static TrafficBucket<MinuteKey> M(Guid a, string minute, ulong s, ulong r) => new(new MinuteKey(a, minute), s, r);

    private static TrafficBucket<DayKey> D(Guid a, string day, ulong s, ulong r) => new(new DayKey(a, day), s, r);

    [Fact]
    public void Write_ThenReadMinutes_SumsSelectedAdapters()
    {
        using var store = new SqliteHistoryStore(DbPath, new TestLog());
        store.Write([M(A, "2026-11-12T10:00", 10, 100), M(B, "2026-11-12T10:00", 1, 1), M(A, "2026-11-12T10:01", 5, 50)], []);
        Assert.Equal(
            [new HistoryPoint("2026-11-12T10:00", 11, 101), new HistoryPoint("2026-11-12T10:01", 5, 50)],
            store.ReadMinutes([A, B], "2026-11-12T10:00", "2026-11-12T10:59"));
        Assert.Equal([new HistoryPoint("2026-11-12T10:00", 1, 1)], store.ReadMinutes([B], "2026-11-12T10:00", "2026-11-12T10:59"));
    }

    [Fact]
    public void Write_SameKeyTwice_Adds()
    {
        using var store = new SqliteHistoryStore(DbPath, new TestLog());
        store.Write([M(A, "2026-11-12T10:00", 10, 100)], [D(A, "2026-11-12", 10, 100)]);
        store.Write([M(A, "2026-11-12T10:00", 5, 5)], [D(A, "2026-11-12", 5, 5)]);
        Assert.Equal(new HistoryPoint("2026-11-12T10:00", 15, 105), Assert.Single(store.ReadMinutes([A], "2026-11-12T00:00", "2026-11-12T23:59")));
        Assert.Equal(new HistoryPoint("2026-11-12", 15, 105), Assert.Single(store.ReadDays([A], "2026-11-01", "2026-11-30")));
    }

    [Fact]
    public void ReadDays_Range_IsInclusiveAndOrdered()
    {
        using var store = new SqliteHistoryStore(DbPath, new TestLog());
        store.Write([], [D(A, "2026-11-10", 1, 1), D(A, "2026-11-12", 3, 3), D(A, "2026-11-11", 2, 2), D(A, "2026-11-13", 4, 4)]);
        Assert.Equal(["2026-11-10", "2026-11-11", "2026-11-12"], store.ReadDays([A], "2026-11-10", "2026-11-12").Select(p => p.LocalTime));
    }

    [Fact]
    public void Purge_RemovesOlderThanCutoffs()
    {
        using var store = new SqliteHistoryStore(DbPath, new TestLog());
        store.Write([M(A, "2026-11-04T23:59", 1, 1), M(A, "2026-11-05T10:15", 2, 2)], [D(A, "2026-09-10", 1, 1), D(A, "2026-09-11", 2, 2)]);
        store.Purge("2026-11-05T10:15", "2026-09-11");
        Assert.Equal(["2026-11-05T10:15"], store.ReadMinutes([A], "2026-01-01T00:00", "2026-12-31T23:59").Select(p => p.LocalTime));
        Assert.Equal(["2026-09-11"], store.ReadDays([A], "2026-01-01", "2026-12-31").Select(p => p.LocalTime));
    }

    [Fact]
    public void Open_CorruptFile_RenamesAndRecreates()
    {
        File.WriteAllText(DbPath, "this is not a sqlite database at all, just garbage bytes");
        var log = new TestLog();
        using (var store = new SqliteHistoryStore(DbPath, log))
        {
            store.Write([M(A, "2026-11-12T10:00", 1, 1)], []);
            Assert.Single(store.ReadMinutes([A], "2026-11-12T00:00", "2026-11-12T23:59"));
        }

        Assert.Single(Directory.GetFiles(_dir, "history.db.corrupt-*"));
        Assert.Contains(log.Lines, l => l.StartsWith("WARN", StringComparison.Ordinal));
    }

    [Fact]
    public void Open_HealthyFileWithoutReadAccess_ThrowsAndKeepsData()
    {
        // Reproduksi temuan instalasi: ACL file rusak (akses baca ditolak) TIDAK boleh dianggap database korup;
        // mengkarantina file sehat = riwayat hilang.
        using (var store = new SqliteHistoryStore(DbPath, new TestLog()))
        {
            store.Write([M(A, "2026-11-12T10:00", 1, 1)], []);
        }

        SqliteConnection.ClearAllPools();
        var file = new FileInfo(DbPath);
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(me, System.Security.AccessControl.FileSystemRights.ReadData, System.Security.AccessControl.AccessControlType.Deny);
        var acl = file.GetAccessControl();
        acl.AddAccessRule(deny);
        file.SetAccessControl(acl);
        try
        {
            Assert.ThrowsAny<Exception>(() => new SqliteHistoryStore(DbPath, new TestLog()).Dispose());
            Assert.True(File.Exists(DbPath));
            Assert.Empty(Directory.GetFiles(_dir, "history.db.corrupt-*"));
        }
        finally
        {
            acl = file.GetAccessControl();
            acl.RemoveAccessRule(deny);
            file.SetAccessControl(acl);
        }
    }

    [Fact]
    public void Read_EmptyAdapterList_Throws()
    {
        using var store = new SqliteHistoryStore(DbPath, new TestLog());
        Assert.Throws<ArgumentException>(() => store.ReadMinutes([], "2026-11-12T00:00", "2026-11-12T23:59"));
        Assert.Throws<ArgumentException>(() => store.ReadDays(Enumerable.Range(0, 65).Select(_ => Guid.NewGuid()).ToList(), "2026-11-01", "2026-11-30"));
    }

    [Fact]
    public void Read_InjectionInRangeText_IsTreatedAsData()
    {
        using var store = new SqliteHistoryStore(DbPath, new TestLog());
        store.Write([M(A, "2026-11-12T10:00", 1, 1)], []);
        Assert.Empty(store.ReadMinutes([A], "x' OR '1'='1", "x' OR '1'='1"));
        Assert.Single(store.ReadMinutes([A], "2026-11-12T00:00", "2026-11-12T23:59"));
    }

    [Fact]
    public void Open_SetsWalMode()
    {
        using var store = new SqliteHistoryStore(DbPath, new TestLog());
        Assert.Equal("wal", store.JournalMode());
    }

    [Fact]
    public void Reopen_KeepsData()
    {
        using (var store = new SqliteHistoryStore(DbPath, new TestLog()))
        {
            store.Write([M(A, "2026-11-12T10:00", 9, 9)], []);
        }

        using var reopened = new SqliteHistoryStore(DbPath, new TestLog());
        Assert.Equal(9UL, Assert.Single(reopened.ReadMinutes([A], "2026-11-12T00:00", "2026-11-12T23:59")).SentBytes);
    }
}
