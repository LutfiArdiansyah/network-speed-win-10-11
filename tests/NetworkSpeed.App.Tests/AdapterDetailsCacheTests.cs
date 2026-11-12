using NetworkSpeed.App.Features;
using NetworkSpeed.Core.Adapters;

namespace NetworkSpeed.App.Tests;

public sealed class AdapterDetailsCacheTests
{
    private static readonly AdapterInfo Wifi = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "Wi-Fi", AdapterKind.Wireless, true, true);
    private static readonly AdapterInfo Ethernet = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), "Ethernet", AdapterKind.Ethernet, true, true);

    private int _ssidReads;
    private int _ipReads;
    private DateTime _now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);

    private AdapterDetailsCache Create() => new(
        _ => { _ssidReads++; return "Office"; },
        _ => { _ipReads++; return "10.0.0.2"; },
        () => _now);

    [Fact]
    public void Get_WithoutSsid_NeverReadsWlan()
    {
        // Membaca SSID = akses lokasi di Windows 11 24H2+ (icon lokasi muncul); tooltip tertutup tidak boleh memicunya.
        var cache = Create();
        for (var i = 0; i < 120; i++)
        {
            _now = _now.AddSeconds(1);
            Assert.Equal((null, "10.0.0.2"), cache.Get(Wifi, includeSsid: false));
        }

        Assert.Equal(0, _ssidReads);
    }

    [Fact]
    public void Get_WithSsid_ReadsOncePerLifetime()
    {
        var cache = Create();
        Assert.Equal(("Office", "10.0.0.2"), cache.Get(Wifi, includeSsid: true));
        _now = _now.AddSeconds(29);
        Assert.Equal(("Office", "10.0.0.2"), cache.Get(Wifi, includeSsid: true));
        Assert.Equal(1, _ssidReads);

        _now = _now.AddSeconds(2);
        cache.Get(Wifi, includeSsid: true);
        Assert.Equal(2, _ssidReads);
    }

    [Fact]
    public void Get_SsidReadThenClosed_DoesNotReadAgain()
    {
        var cache = Create();
        cache.Get(Wifi, includeSsid: true);
        _now = _now.AddMinutes(5);
        Assert.Equal((null, "10.0.0.2"), cache.Get(Wifi, includeSsid: false));
        Assert.Equal(1, _ssidReads);
    }

    [Fact]
    public void Get_Ipv4_CachedAcrossSsidModes()
    {
        var cache = Create();
        cache.Get(Wifi, includeSsid: false);
        cache.Get(Wifi, includeSsid: true);
        Assert.Equal(1, _ipReads);
    }

    [Fact]
    public void Get_Ethernet_NeverReadsSsid()
    {
        var cache = Create();
        Assert.Equal((null, "10.0.0.2"), cache.Get(Ethernet, includeSsid: true));
        Assert.Equal(0, _ssidReads);
    }

    [Fact]
    public void Clear_ForcesFreshRead()
    {
        var cache = Create();
        cache.Get(Wifi, includeSsid: true);
        cache.Clear();
        cache.Get(Wifi, includeSsid: true);
        Assert.Equal(2, _ssidReads);
        Assert.Equal(2, _ipReads);
    }
}
