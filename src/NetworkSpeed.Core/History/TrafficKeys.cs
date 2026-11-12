using System.Globalization;

namespace NetworkSpeed.Core.History;

/// <summary>Kunci agregat per menit: adapter + menit lokal <c>yyyy-MM-ddTHH:mm</c> (spec H-05, E-15).</summary>
public readonly record struct MinuteKey(Guid AdapterId, string LocalMinute);

/// <summary>Kunci total per hari: adapter + tanggal lokal <c>yyyy-MM-dd</c>.</summary>
public readonly record struct DayKey(Guid AdapterId, string LocalDay);

/// <summary>Jumlah byte untuk satu kunci.</summary>
public sealed record TrafficBucket<TKey>(TKey Key, ulong SentBytes, ulong ReceivedBytes);

/// <summary>Format waktu lokal untuk kunci; string-nya sebanding secara leksikografis.</summary>
public static class LocalTimeKey
{
    public const string MinuteFormat = "yyyy-MM-dd'T'HH:mm";
    public const string DayFormat = "yyyy-MM-dd";

    /// <summary>Panjang kunci menit, mis. <c>2026-11-12T10:15</c>.</summary>
    public const int MinuteKeyLength = 16;

    /// <summary>Panjang kunci hari, mis. <c>2026-11-12</c>.</summary>
    public const int DayKeyLength = 10;

    public static string Minute(DateTime local) => local.ToString(MinuteFormat, CultureInfo.InvariantCulture);

    public static string Day(DateTime local) => local.ToString(DayFormat, CultureInfo.InvariantCulture);
}
