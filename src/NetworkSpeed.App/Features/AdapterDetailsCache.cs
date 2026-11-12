using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Interop.Wlan;

namespace NetworkSpeed.App.Features;

/// <summary>
/// SSID dan IPv4 adapter untuk tooltip (spec F-06), di-cache 30 detik agar API WLAN/IP tidak dipanggil tiap detik.
/// SSID hanya dibaca bila diminta (tooltip sedang terbuka): sejak Windows 11 24H2 membaca SSID dihitung sebagai akses
/// lokasi dan memunculkan icon lokasi di taskbar. Nilai ini hanya untuk ditampilkan; tidak boleh dicatat ke log (spec §7).
/// </summary>
public sealed class AdapterDetailsCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private readonly Func<Guid, string?> _readSsid;
    private readonly Func<Guid, string?> _readIpv4;
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<Guid, (string? Value, DateTime ReadAt)> _ssids = [];
    private readonly Dictionary<Guid, (string? Value, DateTime ReadAt)> _ipv4s = [];

    public AdapterDetailsCache()
        : this(WlanSsidReader.TryReadSsid, ReadIpv4, () => DateTime.UtcNow)
    {
    }

    public AdapterDetailsCache(Func<Guid, string?> readSsid, Func<Guid, string?> readIpv4, Func<DateTime> utcNow)
    {
        ArgumentNullException.ThrowIfNull(readSsid);
        ArgumentNullException.ThrowIfNull(readIpv4);
        ArgumentNullException.ThrowIfNull(utcNow);
        _readSsid = readSsid;
        _readIpv4 = readIpv4;
        _utcNow = utcNow;
    }

    /// <param name="adapter">Adapter yang ditampilkan.</param>
    /// <param name="includeSsid">true hanya saat tooltip terlihat; false = API WLAN tidak disentuh dan SSID null.</param>
    public (string? Ssid, string? Ipv4) Get(AdapterInfo adapter, bool includeSsid)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var now = _utcNow();
        var ipv4 = GetCached(_ipv4s, adapter.Id, now, _readIpv4);
        var ssid = includeSsid && adapter.Kind == AdapterKind.Wireless ? GetCached(_ssids, adapter.Id, now, _readSsid) : null;
        return (ssid, ipv4);
    }

    /// <summary>Dipanggil saat jaringan berubah.</summary>
    public void Clear()
    {
        _ssids.Clear();
        _ipv4s.Clear();
    }

    private static string? GetCached(Dictionary<Guid, (string? Value, DateTime ReadAt)> cache, Guid id, DateTime now, Func<Guid, string?> read)
    {
        if (cache.TryGetValue(id, out var cached) && now - cached.ReadAt < Lifetime)
        {
            return cached.Value;
        }

        var value = read(id);
        cache[id] = (value, now);
        return value;
    }

    private static string? ReadIpv4(Guid id)
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => Guid.TryParse(n.Id, out var g) && g == id);
            return nic?.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }
}
