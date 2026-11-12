using System.ComponentModel;
using System.Net.NetworkInformation;
using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Sampling;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.IpHelper;
using Windows.Win32.NetworkManagement.Ndis;

namespace NetworkSpeed.Interop;

/// <summary>
/// Membaca counter 64-bit per adapter lewat <c>GetIfTable2</c> (spec F-01).
/// Filter interface diabaikan agar trafik tidak terhitung ganda.
/// </summary>
public sealed class IfTableCounterSource : IInterfaceCounterSource
{
    public IReadOnlyList<InterfaceCounterSnapshot> ReadCounters() =>
        ReadRows(static (in MIB_IF_ROW2 row, AdapterKind _) =>
            new InterfaceCounterSnapshot(row.InterfaceGuid, row.InOctets, row.OutOctets));

    public IReadOnlyList<AdapterInfo> ReadAdapters()
    {
        var gatewayById = ReadDefaultGatewayFlags();
        return ReadRows((in MIB_IF_ROW2 row, AdapterKind kind) => new AdapterInfo(
            row.InterfaceGuid,
            row.Alias.ToString(),
            kind,
            row.OperStatus == IF_OPER_STATUS.IfOperStatusUp,
            gatewayById.GetValueOrDefault(row.InterfaceGuid)));
    }

    private delegate T RowMapper<out T>(in MIB_IF_ROW2 row, AdapterKind kind);

    private static unsafe List<T> ReadRows<T>(RowMapper<T> map)
    {
        MIB_IF_TABLE2* table = null;
        try
        {
            var error = PInvoke.GetIfTable2(&table);
            if (error != WIN32_ERROR.NO_ERROR)
            {
                throw new Win32Exception((int)error, "GetIfTable2 failed while reading network interface counters.");
            }

            var rows = table->Table.AsSpan(checked((int)table->NumEntries));
            var result = new List<T>(rows.Length);
            var seen = new HashSet<Guid>();
            foreach (ref readonly var row in rows)
            {
                var kind = AdapterClassifier.Classify(
                    row.Type,
                    row.InterfaceAndOperStatusFlags.HardwareInterface,
                    row.InterfaceAndOperStatusFlags.FilterInterface);
                if (kind is null || !seen.Add(row.InterfaceGuid))
                {
                    continue;
                }

                result.Add(map(in row, kind.Value));
            }

            return result;
        }
        finally
        {
            if (table is not null)
            {
                PInvoke.FreeMibTable(table);
            }
        }
    }

    // Lookup O(1) per adapter; GetIfTable2 tidak memuat gateway, jadi diambil dari API managed.
    private static Dictionary<Guid, bool> ReadDefaultGatewayFlags()
    {
        var flags = new Dictionary<Guid, bool>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (Guid.TryParse(nic.Id, out var id))
            {
                flags[id] = nic.GetIPProperties().GatewayAddresses.Count > 0;
            }
        }

        return flags;
    }
}
