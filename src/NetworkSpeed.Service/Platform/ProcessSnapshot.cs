using System.ComponentModel;
using System.Diagnostics;
using NetworkSpeed.Core.PerApp;

namespace NetworkSpeed.Service.Platform;

/// <summary>
/// PID → (sesi, nama proses) dari snapshot sistem (<c>NtQuerySystemInformation</c> via <see cref="Process.GetProcesses()"/>),
/// tanpa membuka handle proses, sehingga bekerja untuk akun LocalService (hasil spike, spec S-05).
/// </summary>
public static class ProcessSnapshot
{
    public static Dictionary<int, ProcessIdentity> Read()
    {
        var map = new Dictionary<int, ProcessIdentity>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    map[process.Id] = new ProcessIdentity(process.SessionId, process.ProcessName);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // Proses keluar saat dibaca; byte-nya akan masuk "System" (fail closed).
                }
            }
        }

        return map;
    }
}
