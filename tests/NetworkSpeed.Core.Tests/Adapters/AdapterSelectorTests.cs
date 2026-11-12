using NetworkSpeed.Core.Adapters;

namespace NetworkSpeed.Core.Tests.Adapters;

public class AdapterSelectorTests
{
    private static AdapterInfo Ad(int n, AdapterKind kind, bool up = true, bool gateway = true, string? name = null)
        => new(new Guid(n, 0, 0, new byte[8]), name ?? $"nic{n}", kind, up, gateway);

    [Fact]
    public void Auto_PicksUpPhysicalWithGateway_IgnoresVpnWhenPhysicalExists()
    {
        AdapterInfo[] list = [Ad(1, AdapterKind.Ethernet), Ad(2, AdapterKind.Vpn), Ad(3, AdapterKind.Wireless, gateway: false)];
        Assert.Equal([list[0].Id], AdapterSelector.Select(list, AdapterSelectionMode.Auto, null, includeVpnInAll: false));
    }

    [Fact]
    public void Auto_FallsBackToVpn_WhenNoPhysicalHasGateway()
    {
        AdapterInfo[] list = [Ad(1, AdapterKind.Ethernet, gateway: false), Ad(2, AdapterKind.Vpn)];
        Assert.Equal([list[1].Id], AdapterSelector.Select(list, AdapterSelectionMode.Auto, null, includeVpnInAll: false));
    }

    [Fact]
    public void Auto_NeverPicksLoopback()
    {
        AdapterInfo[] list = [Ad(1, AdapterKind.Loopback)];
        Assert.Empty(AdapterSelector.Select(list, AdapterSelectionMode.Auto, null, includeVpnInAll: true));
    }

    [Fact]
    public void Auto_FallsBackToVirtualWithGateway_WhenHyperVExternalSwitchCarriesTraffic()
    {
        // Hyper-V external vSwitch: IP & gateway pindah ke vEthernet (Virtual); NIC fisik Up tanpa gateway.
        AdapterInfo[] list = [Ad(1, AdapterKind.Ethernet, gateway: false), Ad(2, AdapterKind.Virtual), Ad(3, AdapterKind.Virtual, gateway: false)];
        Assert.Equal([list[1].Id], AdapterSelector.Select(list, AdapterSelectionMode.Auto, null, includeVpnInAll: false));
    }

    [Fact]
    public void Auto_PrefersPhysicalOverVirtual_WhenBothHaveGateway()
    {
        AdapterInfo[] list = [Ad(1, AdapterKind.Virtual), Ad(2, AdapterKind.Wireless)];
        Assert.Equal([list[1].Id], AdapterSelector.Select(list, AdapterSelectionMode.Auto, null, includeVpnInAll: false));
    }

    [Fact]
    public void All_ExcludesLoopbackVirtualAndDown_AndVpnByDefault()
    {
        AdapterInfo[] list =
        [
            Ad(1, AdapterKind.Ethernet), Ad(2, AdapterKind.Loopback), Ad(3, AdapterKind.Virtual),
            Ad(4, AdapterKind.Wireless, up: false), Ad(5, AdapterKind.Vpn),
        ];
        Assert.Equal([list[0].Id], AdapterSelector.Select(list, AdapterSelectionMode.All, null, includeVpnInAll: false));
        Assert.Equal(
            new HashSet<Guid> { list[0].Id, list[4].Id },
            AdapterSelector.Select(list, AdapterSelectionMode.All, null, includeVpnInAll: true).ToHashSet());
    }

    [Fact]
    public void Specific_SurvivesRename()
    {
        var renamed = Ad(7, AdapterKind.Ethernet, name: "Renamed NIC");
        Assert.Equal([renamed.Id], AdapterSelector.Select([renamed], AdapterSelectionMode.Specific, renamed.Id, includeVpnInAll: false));
    }

    [Fact]
    public void Specific_MissingOrDown_ReturnsEmpty()
    {
        var down = Ad(1, AdapterKind.Ethernet, up: false);
        Assert.Empty(AdapterSelector.Select([down], AdapterSelectionMode.Specific, down.Id, includeVpnInAll: false));
        Assert.Empty(AdapterSelector.Select([down], AdapterSelectionMode.Specific, Guid.NewGuid(), includeVpnInAll: false));
        Assert.Empty(AdapterSelector.Select([down], AdapterSelectionMode.Specific, null, includeVpnInAll: false));
    }

    [Fact]
    public void AnyMode_EmptyList_ReturnsEmpty()
    {
        foreach (var mode in Enum.GetValues<AdapterSelectionMode>())
        {
            Assert.Empty(AdapterSelector.Select([], mode, null, includeVpnInAll: true));
        }
    }
}
