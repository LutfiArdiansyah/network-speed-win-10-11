using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NetworkSpeed.Interop.Shell;

/// <summary>Status shell Windows yang memengaruhi visibilitas & warna overlay (spec D-01, D-05, E-01).</summary>
public static partial class ShellState
{
    private const uint AbmGetState = 0x4;
    private const int AbsAutoHide = 0x1;
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // QUERY_USER_NOTIFICATION_STATE: QUNS_BUSY (2), QUNS_RUNNING_D3D_FULL_SCREEN (3), QUNS_PRESENTATION_MODE (4).
    private static readonly int[] FullscreenStates = [2, 3, 4];

    /// <summary>Ada aplikasi fullscreen/presentasi aktif.</summary>
    public static bool IsFullscreenAppActive() =>
        SHQueryUserNotificationState(out var state) == 0 && FullscreenStates.Contains(state);

    public static bool IsTaskbarAutoHide()
    {
        var data = new AppBarData { CbSize = Marshal.SizeOf<AppBarData>() };
        return ((int)SHAppBarMessage(AbmGetState, ref data) & AbsAutoHide) != 0;
    }

    /// <summary>Tema taskbar terang (<c>SystemUsesLightTheme</c> = 1). Nilai tidak ada → gelap (default Windows 11).</summary>
    public static bool IsLightTheme() =>
        Registry.GetValue(PersonalizeKey, "SystemUsesLightTheme", 0) is int value && value == 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int CbSize;
        public IntPtr Hwnd;
        public uint CallbackMessage;
        public uint Edge;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public IntPtr LParam;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);

    [DllImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
}
