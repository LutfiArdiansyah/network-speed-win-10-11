namespace NetworkSpeed.Core.Taskbar;

/// <summary>
/// Snapshot layout satu taskbar dalam piksel fisik. Tidak memuat nama atau ID aplikasi (spec F-07d).
/// </summary>
/// <param name="MonitorId">Nama device monitor, mis. <c>\\.\DISPLAY1</c>.</param>
/// <param name="HasUiaDetail">
/// <c>false</c> bila hanya data Win32 yang tersedia (UI Automation gagal): posisi tombol tidak diketahui (F-07b).
/// </param>
/// <param name="WidgetsRight">Tepi kanan tombol Widgets di kiri Start (Win11 rata tengah), bila ada.</param>
/// <param name="FirstButtonLeft">Tepi kiri tombol paling kiri di grup tengah (Start/Search).</param>
/// <param name="LastAppRight">Tepi kanan tombol app terakhir.</param>
/// <param name="TrayLeft">Tepi kiri area tray (atau tepi kanan taskbar bila tidak ada tray).</param>
public sealed record TaskbarLayout(
    string MonitorId,
    bool IsPrimary,
    PixelRect Taskbar,
    TaskbarEdge Edge,
    bool IsWindows11,
    bool IsCentered,
    bool HasUiaDetail,
    int? WidgetsRight,
    int? FirstButtonLeft,
    int? LastAppRight,
    int TrayLeft)
{
    public bool IsVertical => Edge is TaskbarEdge.Left or TaskbarEdge.Right;
}
