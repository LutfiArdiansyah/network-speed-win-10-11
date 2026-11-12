using NetworkSpeed.Core.Adapters;

namespace NetworkSpeed.Core.Sampling;

/// <summary>Satu hasil sampling untuk ditampilkan overlay.</summary>
/// <param name="IsOffline">Tidak ada adapter terpilih yang aktif (spec E-10).</param>
/// <param name="SessionSentBytes">Total byte terkirim sejak aplikasi berjalan (tooltip F-06).</param>
public sealed record SpeedReading(
    double UpBytesPerSecond,
    double DownBytesPerSecond,
    bool IsOffline,
    IReadOnlyList<AdapterInfo> SelectedAdapters,
    ulong SessionSentBytes,
    ulong SessionReceivedBytes);
