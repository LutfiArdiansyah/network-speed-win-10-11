namespace NetworkSpeed.Core.History;

/// <summary>Masa simpan riwayat (spec H-05): menit 7 hari, total harian 62 hari (untuk kuota bulanan).</summary>
public static class RetentionPolicy
{
    public const int MinuteDays = 7;
    public const int DailyDays = 62;

    /// <summary>Baris menit dengan kunci lebih kecil dari ini dihapus.</summary>
    public static string MinuteCutoff(DateTime localNow) => LocalTimeKey.Minute(localNow.AddDays(-MinuteDays));

    /// <summary>Baris harian dengan kunci lebih kecil dari ini dihapus.</summary>
    public static string DayCutoff(DateTime localNow) => LocalTimeKey.Day(localNow.Date.AddDays(-DailyDays));
}
