using System.Globalization;
using System.Text.Json;

namespace NetworkSpeed.Core.Storage;

/// <summary>
/// Status notifikasi kuota yang sudah dikirim, per user (<c>%LOCALAPPDATA%\NetworkSpeed\state.json</c>), terpisah dari
/// settings agar mengedit settings tidak mereset status. Kunci berumur &gt; 62 hari dibuang saat menyimpan.
/// File rusak di-rename ke <c>.corrupt-&lt;waktu&gt;</c> lalu dimulai kosong (pola spec E-11). Dipakai dari satu thread.
/// </summary>
public sealed class NotificationStateStore(string path, TimeProvider time)
{
    private const int RetentionDays = 62;
    private const string DateFormat = "yyyy-MM-dd";
    private Dictionary<string, string>? _sent;

    public IReadOnlySet<string> Load() => Sent().Keys.ToHashSet(StringComparer.Ordinal);

    public void MarkSent(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var sent = Sent();
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        foreach (var key in keys)
        {
            sent[key] = today.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        var cutoff = today.AddDays(-RetentionDays);
        foreach (var stale in sent.Where(kv => !DateOnly.TryParseExact(kv.Value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d < cutoff).Select(kv => kv.Key).ToList())
        {
            sent.Remove(stale);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>> { ["sent"] = sent }));
        File.Move(temp, path, overwrite: true);
    }

    private Dictionary<string, string> Sent()
    {
        if (_sent is not null)
        {
            return _sent;
        }

        var loaded = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return _sent = loaded;
        }

        // IOException/UnauthorizedAccessException (file terkunci) sengaja tidak ditangkap & hasil tidak di-cache:
        // pemanggil mencoba lagi nanti, dan status lama tidak tertimpa oleh state kosong.
        var text = File.ReadAllText(path);
        try
        {
            var document = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(text);
            if (document?.TryGetValue("sent", out var sent) == true && sent is not null)
            {
                foreach (var (key, value) in sent)
                {
                    loaded[key] = value;
                }
            }
        }
        catch (JsonException)
        {
            var stamp = time.GetLocalNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(path, $"{path}.corrupt-{stamp}", overwrite: true);
        }

        return _sent = loaded;
    }
}
