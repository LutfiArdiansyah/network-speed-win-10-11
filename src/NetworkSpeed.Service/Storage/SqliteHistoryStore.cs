using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkSpeed.Core.History;
using NetworkSpeed.Core.Logging;

namespace NetworkSpeed.Service.Storage;

/// <summary>
/// Riwayat per PC di SQLite (spec H-05, H-06): tabel menit & harian per adapter, WAL, upsert-add dalam satu transaksi.
/// Semua SQL berparameter; daftar adapter 1–64. File rusak di-rename ke <c>.corrupt-&lt;waktu&gt;</c> (maks 3) lalu dibuat
/// baru (E-11). Thread-safe: satu koneksi dijaga lock (penulis = loop riwayat, pembaca = handler pipe).
/// </summary>
public sealed class SqliteHistoryStore : IHistoryStore, IDisposable
{
    public const int MaxAdapters = 64;
    private const int SchemaVersion = 1;
    private const int MaxCorruptFiles = 3;

    private readonly string _path;
    // Kode error SQLite yang berarti file benar-benar rusak (https://sqlite.org/rescode.html).
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    private readonly IAppLog _log;
    private readonly Lock _gate = new();
    private readonly SqliteConnection _connection;

    public SqliteHistoryStore(string databasePath, IAppLog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(log);
        _path = databasePath;
        _log = log;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connection = OpenHealthyConnection();
        CreateSchema();
    }

    public void Write(IReadOnlyList<TrafficBucket<MinuteKey>> minutes, IReadOnlyList<TrafficBucket<DayKey>> days)
    {
        ArgumentNullException.ThrowIfNull(minutes);
        ArgumentNullException.ThrowIfNull(days);
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();
            Upsert(transaction, "minute_traffic", "minute_local", minutes.Select(m => (m.Key.AdapterId, m.Key.LocalMinute, m.SentBytes, m.ReceivedBytes)));
            Upsert(transaction, "daily_traffic", "day_local", days.Select(d => (d.Key.AdapterId, d.Key.LocalDay, d.SentBytes, d.ReceivedBytes)));
            transaction.Commit();
        }
    }

    public void Purge(string minuteCutoff, string dayCutoff)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM minute_traffic WHERE minute_local < $m; DELETE FROM daily_traffic WHERE day_local < $d;";
            command.Parameters.AddWithValue("$m", minuteCutoff);
            command.Parameters.AddWithValue("$d", dayCutoff);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<HistoryPoint> ReadMinutes(IReadOnlyCollection<Guid> adapters, string fromLocalMinute, string toLocalMinute) =>
        Read("minute_traffic", "minute_local", adapters, fromLocalMinute, toLocalMinute);

    public IReadOnlyList<HistoryPoint> ReadDays(IReadOnlyCollection<Guid> adapters, string fromLocalDay, string toLocalDay) =>
        Read("daily_traffic", "day_local", adapters, fromLocalDay, toLocalDay);

    /// <summary>Mode journal aktif (untuk verifikasi WAL).</summary>
    public string JournalMode()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }

    private static string AdapterText(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    // Nama tabel/kolom hanya dari konstanta internal (allowlist), bukan dari input; nilai selalu berparameter.
    private void Upsert(SqliteTransaction transaction, string table, string timeColumn, IEnumerable<(Guid Adapter, string Time, ulong Sent, ulong Received)> rows)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT INTO {table} (adapter_id, {timeColumn}, sent, received) VALUES ($a, $t, $s, $r) " +
            $"ON CONFLICT(adapter_id, {timeColumn}) DO UPDATE SET sent = sent + excluded.sent, received = received + excluded.received;";
        var adapter = command.Parameters.Add("$a", SqliteType.Text);
        var time = command.Parameters.Add("$t", SqliteType.Text);
        var sent = command.Parameters.Add("$s", SqliteType.Integer);
        var received = command.Parameters.Add("$r", SqliteType.Integer);
        command.Prepare();
        foreach (var row in rows)
        {
            adapter.Value = AdapterText(row.Adapter);
            time.Value = row.Time;
            sent.Value = checked((long)row.Sent);
            received.Value = checked((long)row.Received);
            command.ExecuteNonQuery();
        }
    }

    private List<HistoryPoint> Read(string table, string timeColumn, IReadOnlyCollection<Guid> adapters, string from, string to)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        if (adapters.Count is 0 or > MaxAdapters)
        {
            throw new ArgumentException($"Between 1 and {MaxAdapters} adapters are required.", nameof(adapters));
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            var names = adapters.Select((_, i) => "$a" + i.ToString(CultureInfo.InvariantCulture)).ToList();
            command.CommandText =
                $"SELECT {timeColumn}, SUM(sent), SUM(received) FROM {table} " +
                $"WHERE adapter_id IN ({string.Join(", ", names)}) AND {timeColumn} BETWEEN $from AND $to " +
                $"GROUP BY {timeColumn} ORDER BY {timeColumn};";
            var index = 0;
            foreach (var id in adapters)
            {
                command.Parameters.AddWithValue(names[index++], AdapterText(id));
            }

            command.Parameters.AddWithValue("$from", from);
            command.Parameters.AddWithValue("$to", to);
            using var reader = command.ExecuteReader();
            var points = new List<HistoryPoint>();
            while (reader.Read())
            {
                points.Add(new HistoryPoint(reader.GetString(0), (ulong)reader.GetInt64(1), (ulong)reader.GetInt64(2)));
            }

            return points;
        }
    }

    private SqliteConnection OpenHealthyConnection()
    {
        if (File.Exists(_path) && !IsHealthy())
        {
            Quarantine();
        }

        var connection = new SqliteConnection(ConnectionString());
        connection.Open();
        return connection;
    }

    private bool IsHealthy()
    {
        try
        {
            using var probe = new SqliteConnection(ConnectionString());
            probe.Open();
            using var command = probe.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            return string.Equals(Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase)
        {
            return false;
        }

        // Error lain (akses ditolak, file terkunci, I/O) bukan bukti korup: dilempar agar service gagal start dan
        // di-restart (S-07), bukan mengkarantina riwayat yang sehat.
    }

    private void Quarantine()
    {
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var target = $"{_path}.corrupt-{stamp}";
        for (var n = 1; File.Exists(target); n++)
        {
            target = $"{_path}.corrupt-{stamp}-{n.ToString(CultureInfo.InvariantCulture)}";
        }

        File.Move(_path, target);
        foreach (var sidecar in new[] { _path + "-wal", _path + "-shm" })
        {
            File.Delete(sidecar);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        foreach (var old in Directory.GetFiles(directory, Path.GetFileName(_path) + ".corrupt-*").OrderDescending(StringComparer.Ordinal).Skip(MaxCorruptFiles))
        {
            File.Delete(old);
        }

        _log.Warn("History database was corrupt; renamed and recreated.");
    }

    // Pooling dimatikan agar handle file dilepas saat Dispose (rename file rusak, uninstall).
    private string ConnectionString() => new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString();

    private void CreateSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; " +
            "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL); " +
            "CREATE TABLE IF NOT EXISTS minute_traffic (adapter_id TEXT NOT NULL, minute_local TEXT NOT NULL, sent INTEGER NOT NULL, received INTEGER NOT NULL, PRIMARY KEY (adapter_id, minute_local)) WITHOUT ROWID; " +
            "CREATE TABLE IF NOT EXISTS daily_traffic (adapter_id TEXT NOT NULL, day_local TEXT NOT NULL, sent INTEGER NOT NULL, received INTEGER NOT NULL, PRIMARY KEY (adapter_id, day_local)) WITHOUT ROWID; " +
            "CREATE INDEX IF NOT EXISTS ix_minute_time ON minute_traffic (minute_local); " +
            "CREATE INDEX IF NOT EXISTS ix_daily_time ON daily_traffic (day_local); " +
            "INSERT INTO schema_version (version) SELECT $v WHERE NOT EXISTS (SELECT 1 FROM schema_version);";
        command.Parameters.AddWithValue("$v", SchemaVersion);
        command.ExecuteNonQuery();
    }
}
