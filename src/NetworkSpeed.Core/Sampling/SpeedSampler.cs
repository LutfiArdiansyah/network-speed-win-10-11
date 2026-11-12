using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Time;

namespace NetworkSpeed.Core.Sampling;

/// <summary>
/// Menggabungkan pembacaan counter, pemilihan adapter, dan perhitungan kecepatan untuk satu loop sampling.
/// Tidak thread-safe; dipanggil dari satu timer.
/// </summary>
public sealed class SpeedSampler
{
    private readonly IInterfaceCounterSource _source;
    private readonly IMonotonicClock _clock;
    private readonly TimeSpan _adapterRefreshInterval;
    private readonly SpeedCalculator _calculator = new();

    private IReadOnlyList<AdapterInfo>? _adapters;
    private TimeSpan _adaptersReadAt;
    private TimeSpan? _lastSampleAt;
    private double _sessionSent;
    private double _sessionReceived;

    public SpeedSampler(IInterfaceCounterSource source, IMonotonicClock clock, TimeSpan adapterRefreshInterval)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);
        _source = source;
        _clock = clock;
        _adapterRefreshInterval = adapterRefreshInterval;
    }

    /// <summary>
    /// Mengambil satu sampel. Mengembalikan <c>null</c> saat baseline (sampel pertama atau setelah resume),
    /// kecuali tidak ada adapter terpilih: selalu reading offline.
    /// </summary>
    public SpeedReading? Sample(AdapterSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        var now = _clock.Elapsed;
        var adapters = GetAdapters(now);
        var selectedIds = AdapterSelector.Select(adapters, selection.Mode, selection.SpecificId, selection.IncludeVpnInAll);
        var rates = _calculator.AddSnapshot(_source.ReadCounters(), now);
        var elapsedSeconds = _lastSampleAt is { } last ? (now - last).TotalSeconds : 0;
        _lastSampleAt = now;

        if (selectedIds.Count == 0)
        {
            return new SpeedReading(0, 0, true, [], SessionSent, SessionReceived);
        }

        if (rates is null)
        {
            return null;
        }

        double up = 0, down = 0;
        foreach (var id in selectedIds)
        {
            if (rates.TryGetValue(id, out var rate))
            {
                up += rate.SentBytesPerSecond;
                down += rate.ReceivedBytesPerSecond;
            }
        }

        _sessionSent += up * elapsedSeconds;
        _sessionReceived += down * elapsedSeconds;
        var selected = adapters.Where(a => selectedIds.Contains(a.Id)).ToList();
        return new SpeedReading(up, down, false, selected, SessionSent, SessionReceived);
    }

    /// <summary>Memaksa daftar adapter dibaca ulang pada sampel berikutnya (mis. event NetworkChange).</summary>
    public void InvalidateAdapters() => _adapters = null;

    /// <summary>Setelah resume dari sleep: baseline baru dan daftar adapter dibaca ulang (spec E-05).</summary>
    public void OnResume()
    {
        _calculator.ResetBaseline();
        _lastSampleAt = null;
        InvalidateAdapters();
    }

    private ulong SessionSent => (ulong)Math.Round(_sessionSent);

    private ulong SessionReceived => (ulong)Math.Round(_sessionReceived);

    private IReadOnlyList<AdapterInfo> GetAdapters(TimeSpan now)
    {
        if (_adapters is null || now - _adaptersReadAt >= _adapterRefreshInterval)
        {
            _adapters = _source.ReadAdapters();
            _adaptersReadAt = now;
        }

        return _adapters;
    }
}
