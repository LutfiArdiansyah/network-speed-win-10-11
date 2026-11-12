using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Interop.Wlan;

namespace NetworkSpeed.App.Features;

/// <summary>
/// SSID dan IPv4 adapter untuk tooltip (spec F-06), di-cache 30 detik agar API WLAN/IP tidak dipanggil tiap detik.
/// Nilai ini hanya untuk ditampilkan; tidak boleh dicatat ke log (spec §7).
/// </summary>
public sealed class AdapterDetailsCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private readonly Dictionary<Guid, (string? Ssid, string? Ipv4, DateTime ReadAt)> _cache = [];

    public (string? Ssid, string? Ipv4) Get(AdapterInfo adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(adapter.Id, out var cached) && now - cached.ReadAt < Lifetime)
        {
            return (cached.Ssid, cached.Ipv4);
        }

        var ssid = adapter.Kind == AdapterKind.Wireless ? WlanSsidReader.TryReadSsid(adapter.Id) : null;
        var ipv4 = ReadIpv4(adapter.Id);
        _cache[adapter.Id] = (ssid, ipv4, now);
        return (ssid, ipv4);
    }

    /// <summary>Dipanggil saat jaringan berubah.</summary>
    public void Clear() => _cache.Clear();

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
