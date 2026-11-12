using System.ComponentModel;
using System.Diagnostics;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Core.Viewer;
using NetworkSpeed.Interop.Shell;

namespace NetworkSpeed.App.Viewer;

/// <summary>
/// Membuka jendela riwayat/per aplikasi di proses viewer terpisah (exe yang sama, <see cref="ViewerCommandLine"/>), agar
/// memori JIT/WPF jendela berat ikut hilang saat jendela ditutup (spec §1). Satu proses per jenis jendela; viewer keluar
/// sendiri saat jendelanya ditutup atau App utama keluar, jadi tidak pernah di-kill dari sini.
/// </summary>
public sealed class ViewerLauncher(IAppLog log) : IDisposable
{
    private readonly Dictionary<ViewerKind, Process> _running = [];

    public void Show(ViewerKind kind, IReadOnlyList<(double Up, double Down)> live)
    {
        if (_running.TryGetValue(kind, out var existing))
        {
            if (IsRunning(existing))
            {
                existing.Refresh();
                ForegroundWindow.BringToFront(existing.MainWindowHandle);
                return;
            }

            existing.Dispose();
            _running.Remove(kind);
        }

        if (Environment.ProcessPath is not { } path)
        {
            log.Error("Viewer could not be started: executable path unknown.");
            return;
        }

        var info = new ProcessStartInfo(path) { UseShellExecute = false };
        foreach (var argument in ViewerCommandLine.Build(new ViewerArguments(kind, Environment.ProcessId, live)))
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            if (Process.Start(info) is { } process)
            {
                _running[kind] = process;
                log.Info($"Viewer started: {kind}.");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            log.Error("Viewer could not be started.", ex);
        }
    }

    /// <summary>Hanya melepas handle; proses viewer menutup dirinya sendiri saat App utama keluar.</summary>
    public void Dispose()
    {
        foreach (var process in _running.Values)
        {
            process.Dispose();
        }

        _running.Clear();
    }

    private static bool IsRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }
}
