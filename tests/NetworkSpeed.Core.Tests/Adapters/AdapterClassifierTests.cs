using NetworkSpeed.Core.Adapters;

namespace NetworkSpeed.Core.Tests.Adapters;

public class AdapterClassifierTests
{
    [Theory]
    [InlineData(6u, true, true)]
    [InlineData(71u, true, true)]
    [InlineData(24u, false, true)]
    public void Classify_FilterInterface_ReturnsNull(uint ifType, bool isHardware, bool isFilter)
        => Assert.Null(AdapterClassifier.Classify(ifType, isHardware, isFilter));

    [Theory]
    [InlineData(24u, false, AdapterKind.Loopback)]
    [InlineData(71u, true, AdapterKind.Wireless)]
    [InlineData(71u, false, AdapterKind.Wireless)]
    [InlineData(6u, true, AdapterKind.Ethernet)]
    [InlineData(23u, false, AdapterKind.Vpn)]
    [InlineData(131u, false, AdapterKind.Vpn)]
    [InlineData(53u, false, AdapterKind.Vpn)]
    [InlineData(6u, false, AdapterKind.Virtual)]
    [InlineData(23u, true, AdapterKind.Other)]
    [InlineData(243u, true, AdapterKind.Other)]
    public void Classify_ByIfTypeAndHardwareFlag(uint ifType, bool isHardware, AdapterKind expected)
        => Assert.Equal(expected, AdapterClassifier.Classify(ifType, isHardware, isFilterInterface: false));
}
