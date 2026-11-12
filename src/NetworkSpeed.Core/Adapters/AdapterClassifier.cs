namespace NetworkSpeed.Core.Adapters;

/// <summary>Menentukan <see cref="AdapterKind"/> dari data <c>MIB_IF_ROW2</c>.</summary>
public static class AdapterClassifier
{
    // Nilai IANA ifType (ipifcons.h).
    private const uint IfTypeEthernetCsmacd = 6;
    private const uint IfTypePpp = 23;
    private const uint IfTypeSoftwareLoopback = 24;
    private const uint IfTypePropVirtual = 53;
    private const uint IfTypeIeee80211 = 71;
    private const uint IfTypeTunnel = 131;

    /// <summary>
    /// Mengembalikan jenis adapter, atau <c>null</c> untuk filter interface (lapisan driver, bukan adapter nyata)
    /// yang harus diabaikan seluruhnya agar trafik tidak terhitung ganda.
    /// </summary>
    public static AdapterKind? Classify(uint ifType, bool isHardwareInterface, bool isFilterInterface)
    {
        if (isFilterInterface)
        {
            return null;
        }

        return ifType switch
        {
            IfTypeSoftwareLoopback => AdapterKind.Loopback,
            IfTypeIeee80211 => AdapterKind.Wireless,
            IfTypeEthernetCsmacd => isHardwareInterface ? AdapterKind.Ethernet : AdapterKind.Virtual,
            IfTypePpp or IfTypeTunnel or IfTypePropVirtual when !isHardwareInterface => AdapterKind.Vpn,
            _ => AdapterKind.Other,
        };
    }
}
