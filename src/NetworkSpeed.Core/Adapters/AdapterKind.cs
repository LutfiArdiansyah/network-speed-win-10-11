namespace NetworkSpeed.Core.Adapters;

/// <summary>Jenis adapter jaringan untuk keperluan pemilihan (spec F-04, F-05).</summary>
public enum AdapterKind
{
    Ethernet,
    Wireless,
    Vpn,
    Virtual,
    Loopback,
    Other,
}
