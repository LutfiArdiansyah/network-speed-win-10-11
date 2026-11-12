using System.Globalization;
using NetworkSpeed.Core.Settings;

namespace NetworkSpeed.Core.Storage;

/// <summary>Hasil memuat file pengaturan.</summary>
public enum SettingsLoadOutcome
{
    Loaded,
    Missing,
    CorruptRecovered,
}

/// <summary>
/// Membaca/menulis <c>settings.json</c>. File rusak di-rename ke <c>.corrupt-&lt;waktu&gt;</c> (maks 3 disimpan)
/// lalu dipakai default (spec E-11). Penulisan atomik lewat file sementara.
/// </summary>
public sealed class SettingsFileStore(string path, TimeProvider time)
{
    private const int MaxCorruptFiles = 3;
    private const string CorruptInfix = ".corrupt-";

    /// <exception cref="IOException">File ada tetapi tidak bisa dibaca (mis. terkunci); pemanggil sebaiknya tidak menimpa.</exception>
    public (UserSettings Settings, SettingsLoadOutcome Outcome) Load()
    {
        if (!File.Exists(path))
        {
            return (new UserSettings(), SettingsLoadOutcome.Missing);
        }

        var text = File.ReadAllText(path);
        try
        {
            return (SettingsSerializer.Deserialize(text), SettingsLoadOutcome.Loaded);
        }
        catch (SettingsFormatException)
        {
            QuarantineCorruptFile();
            return (new UserSettings(), SettingsLoadOutcome.CorruptRecovered);
        }
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, SettingsSerializer.Serialize(SettingsValidator.Normalize(settings)));
        File.Move(temp, path, overwrite: true);
    }

    private void QuarantineCorruptFile()
    {
        var stamp = time.GetLocalNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var target = path + CorruptInfix + stamp;
        for (var n = 1; File.Exists(target); n++)
        {
            target = $"{path}{CorruptInfix}{stamp}-{n.ToString(CultureInfo.InvariantCulture)}";
        }

        File.Move(path, target);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var pattern = Path.GetFileName(path) + CorruptInfix + "*";
        foreach (var old in Directory.GetFiles(directory, pattern).OrderDescending(StringComparer.Ordinal).Skip(MaxCorruptFiles))
        {
            File.Delete(old);
        }
    }
}
