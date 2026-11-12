using System.Globalization;

namespace NetworkSpeed.Core.Quota;

/// <summary>Jenis kuota (spec H-01).</summary>
public enum QuotaKind
{
    Daily,
    Monthly,
}

/// <summary>Satu periode kuota; <see cref="From"/>/<see cref="To"/> inklusif, <see cref="Key"/> unik per periode.</summary>
public sealed record QuotaWindow(QuotaKind Kind, DateOnly From, DateOnly To, string Key);

/// <summary>Menghitung periode kuota berdasarkan tanggal lokal PC.</summary>
public static class QuotaPeriod
{
    private const string DateFormat = "yyyy-MM-dd";

    public static QuotaWindow Daily(DateTime localNow)
    {
        var today = DateOnly.FromDateTime(localNow);
        return new QuotaWindow(QuotaKind.Daily, today, today, "daily:" + Text(today));
    }

    /// <summary>
    /// Periode bulanan yang dimulai pada <paramref name="startDay"/> (1–31). Bulan yang lebih pendek memakai hari
    /// terakhirnya; sebelum tanggal mulai bulan ini, periode masih milik bulan sebelumnya.
    /// </summary>
    public static QuotaWindow Monthly(DateTime localNow, int startDay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startDay, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startDay, 31);

        var today = DateOnly.FromDateTime(localNow);
        var thisMonthStart = StartIn(today.Year, today.Month, startDay);
        var from = today >= thisMonthStart ? thisMonthStart : StartIn(today.AddMonths(-1).Year, today.AddMonths(-1).Month, startDay);
        var nextMonth = new DateOnly(from.Year, from.Month, 1).AddMonths(1);
        var to = StartIn(nextMonth.Year, nextMonth.Month, startDay).AddDays(-1);
        return new QuotaWindow(QuotaKind.Monthly, from, to, "monthly:" + Text(from));
    }

    private static DateOnly StartIn(int year, int month, int startDay) =>
        new(year, month, Math.Min(startDay, DateTime.DaysInMonth(year, month)));

    private static string Text(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
