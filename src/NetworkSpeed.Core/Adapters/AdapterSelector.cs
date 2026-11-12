namespace NetworkSpeed.Core.Adapters;

/// <summary>Memilih adapter yang trafiknya dijumlahkan (spec F-04, F-05).</summary>
public static class AdapterSelector
{
    /// <summary>
    /// Mengembalikan ID adapter terpilih. Set kosong berarti tidak ada adapter yang memenuhi syarat
    /// (pemanggil menampilkan "Offline").
    /// </summary>
    /// <param name="specificId">Dipakai hanya untuk <see cref="AdapterSelectionMode.Specific"/>.</param>
    /// <param name="includeVpnInAll">Hanya berlaku untuk <see cref="AdapterSelectionMode.All"/>.</param>
    public static IReadOnlySet<Guid> Select(
        IReadOnlyCollection<AdapterInfo> adapters,
        AdapterSelectionMode mode,
        Guid? specificId,
        bool includeVpnInAll)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        return mode switch
        {
            AdapterSelectionMode.Auto => SelectAuto(adapters),
            AdapterSelectionMode.All => SelectAll(adapters, includeVpnInAll),
            AdapterSelectionMode.Specific => SelectSpecific(adapters, specificId),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown adapter selection mode."),
        };
    }

    private static HashSet<Guid> SelectAuto(IReadOnlyCollection<AdapterInfo> adapters)
    {
        // Urutan prioritas: fisik → VPN → virtual. VPN hanya dipakai bila tidak ada adapter fisik yang terhubung,
        // agar trafik tidak terhitung dua kali. Virtual menjadi pilihan terakhir untuk Hyper-V external switch,
        // di mana gateway pindah ke vEthernet dan NIC fisik tidak lagi punya gateway.
        foreach (var predicate in AutoTiers)
        {
            var tier = Ids(adapters, a => a.IsUp && a.HasDefaultGateway && predicate(a.Kind));
            if (tier.Count > 0)
            {
                return tier;
            }
        }

        return [];
    }

    private static readonly Func<AdapterKind, bool>[] AutoTiers =
    [
        IsPhysicalKind,
        static kind => kind == AdapterKind.Vpn,
        static kind => kind == AdapterKind.Virtual,
    ];

    private static HashSet<Guid> SelectAll(IReadOnlyCollection<AdapterInfo> adapters, bool includeVpn) =>
        Ids(adapters, a => a.IsUp && (IsPhysicalKind(a.Kind) || (includeVpn && a.Kind == AdapterKind.Vpn)));

    private static HashSet<Guid> SelectSpecific(IReadOnlyCollection<AdapterInfo> adapters, Guid? specificId) =>
        specificId is { } id ? Ids(adapters, a => a.Id == id && a.IsUp) : [];

    private static bool IsPhysicalKind(AdapterKind kind) =>
        kind is AdapterKind.Ethernet or AdapterKind.Wireless or AdapterKind.Other;

    private static HashSet<Guid> Ids(IReadOnlyCollection<AdapterInfo> adapters, Func<AdapterInfo, bool> predicate) =>
        adapters.Where(predicate).Select(a => a.Id).ToHashSet();
}
