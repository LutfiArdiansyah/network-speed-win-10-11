using System.Runtime.InteropServices;

namespace NetworkSpeed.Interop.Shell;

/// <summary>Membawa jendela proses viewer ke depan saat menu dipilih lagi (dipanggil dari App yang sedang foreground).</summary>
public static class ForegroundWindow
{
    private const int SwRestore = 9;

    /// <summary>Tanpa efek bila handle kosong (jendela viewer belum dibuat) atau Windows menolak.</summary>
    public static void BringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SwRestore);
        }

        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
