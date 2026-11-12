namespace NetworkSpeed.Core.Connectivity;

/// <summary>Perubahan status koneksi yang layak dinotifikasi (spec X-02).</summary>
public enum ConnectivityChange
{
    WentOffline,
    CameOnline,
}

/// <summary>
/// Meredam status koneksi yang naik-turun: perubahan baru dilaporkan setelah stabil selama
/// <c>stablePeriod</c>, supaya Wi-Fi yang tidak stabil tidak membanjiri notifikasi.
/// </summary>
public sealed class ConnectivityDebouncer(TimeSpan stablePeriod)
{
    private bool? _stable;
    private bool? _candidate;
    private TimeSpan _candidateSince;

    /// <param name="now">Waktu dari monotonic clock.</param>
    /// <returns>Perubahan yang sudah stabil, atau <c>null</c>.</returns>
    public ConnectivityChange? Update(bool isOnline, TimeSpan now)
    {
        if (_stable is null)
        {
            _stable = isOnline;
            return null;
        }

        if (isOnline == _stable)
        {
            _candidate = null;
            return null;
        }

        if (_candidate != isOnline)
        {
            _candidate = isOnline;
            _candidateSince = now;
            return null;
        }

        if (now - _candidateSince < stablePeriod)
        {
            return null;
        }

        _stable = isOnline;
        _candidate = null;
        return isOnline ? ConnectivityChange.CameOnline : ConnectivityChange.WentOffline;
    }
}
