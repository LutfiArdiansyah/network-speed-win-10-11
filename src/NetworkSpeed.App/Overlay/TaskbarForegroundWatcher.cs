using System.Runtime.InteropServices;

namespace NetworkSpeed.App.Overlay;

/// <summary>
/// Memberi tahu saat taskbar menjadi jendela foreground (diklik), lewat <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c>
/// out-of-context. Callback datang di thread yang memasang hook (UI thread) lewat antrian pesan, tanpa injection.
/// </summary>
public sealed class TaskbarForegroundWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const int ClassNameCapacity = 64;

    // Delegate disimpan di field agar tidak dikumpulkan GC selama hook aktif.
    private readonly WinEventProc _callback;
    private IntPtr _hook;

    public TaskbarForegroundWatcher() => _callback = OnWinEvent;

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    public event Action? TaskbarActivated;

    public void Start()
    {
        if (_hook == IntPtr.Zero)
        {
            _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private static bool IsTaskbar(IntPtr hwnd)
    {
        var buffer = new char[ClassNameCapacity];
        var length = GetClassName(hwnd, buffer, buffer.Length);
        return length > 0
            && new string(buffer, 0, length) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd != IntPtr.Zero && IsTaskbar(hwnd))
        {
            TaskbarActivated?.Invoke();
        }
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetClassName(IntPtr hwnd, [Out] char[] name, int capacity);
}
