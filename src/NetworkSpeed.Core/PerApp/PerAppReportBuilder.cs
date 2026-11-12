using NetworkSpeed.Core.Ipc;

namespace NetworkSpeed.Core.PerApp;

/// <summary>
/// Laporan per aplikasi untuk satu pemanggil (spec S-05, S-09): hanya proses milik sesi pemanggil yang bernama;
/// sesi 0 dan sesi lain digabung ke satu baris "System" tanpa nama. Angka diskalakan proporsional ke total adapter
/// (ETW hanya mengukur payload) sehingga jumlah baris = total adapter.
/// </summary>
public static class PerAppReportBuilder
{
    public static IReadOnlyList<PerAppRow> Build(
        IReadOnlyDictionary<ProcessIdentity, (ulong Sent, ulong Received)> data,
        int callerSessionId,
        ulong adapterSent,
        ulong adapterReceived)
    {
        ArgumentNullException.ThrowIfNull(data);

        var raw = new List<(string Name, bool IsSystem, ulong Sent, ulong Received)>();
        ulong systemSent = 0, systemReceived = 0;
        var hasSystem = false;
        foreach (var (identity, value) in data)
        {
            if (identity.SessionId == callerSessionId && !identity.IsSystem)
            {
                raw.Add((identity.ProcessName, false, value.Sent, value.Received));
                continue;
            }

            hasSystem = true;
            systemSent += value.Sent;
            systemReceived += value.Received;
        }

        if (hasSystem)
        {
            raw.Add((ProcessIdentity.SystemName, true, systemSent, systemReceived));
        }

        var sent = Scale(raw.Select(r => r.Sent).ToList(), adapterSent);
        var received = Scale(raw.Select(r => r.Received).ToList(), adapterReceived);
        return raw
            .Select((r, i) => new PerAppRow(r.Name, r.IsSystem, sent[i], received[i]))
            .OrderByDescending(r => (UInt128)r.SentBytes + r.ReceivedBytes)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Pembagian proporsional dengan UInt128 (tanpa overflow); sisa pembulatan diberikan ke baris terbesar
    // agar jumlahnya tepat sama dengan total adapter.
    private static ulong[] Scale(List<ulong> values, ulong target)
    {
        var result = new ulong[values.Count];
        UInt128 total = 0;
        foreach (var v in values)
        {
            total += v;
        }

        if (total == 0 || values.Count == 0)
        {
            return result;
        }

        ulong assigned = 0;
        var largest = 0;
        for (var i = 0; i < values.Count; i++)
        {
            result[i] = (ulong)(values[i] * (UInt128)target / total);
            assigned += result[i];
            if (values[i] > values[largest])
            {
                largest = i;
            }
        }

        result[largest] += target - assigned;
        return result;
    }
}
