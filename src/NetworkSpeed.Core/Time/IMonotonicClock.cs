namespace NetworkSpeed.Core.Time;

/// <summary>
/// Sumber waktu yang selalu maju dan tidak terpengaruh perubahan jam sistem
/// (DST, sinkronisasi NTP, ubah manual). Dipakai untuk menghitung kecepatan (spec F-01, E-14).
/// </summary>
public interface IMonotonicClock
{
    /// <summary>Waktu sejak titik acuan tetap yang tidak ditentukan.</summary>
    TimeSpan Elapsed { get; }
}
