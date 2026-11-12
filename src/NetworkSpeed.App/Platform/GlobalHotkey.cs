using System.Runtime.InteropServices;
using System.Windows.Interop;
using NetworkSpeed.Core.Input;
using NetworkSpeed.Core.Logging;

namespace NetworkSpeed.App.Platform;

/// <summary>
/// Hotkey global Show/Hide (spec X-03) lewat <c>RegisterHotKey</c> pada jendela message-only.
/// Gagal mendaftar (mis. dipakai aplikasi lain) tidak melempar exception; status dilaporkan ke Settings.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int HotkeyId = 0x4E53;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private readonly IAppLog _log;
    private bool _registered;

    public GlobalHotkey(IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _source = new HwndSource(new HwndSourceParameters("NetworkSpeed.Hotkey") { ParentWindow = HwndMessage, Width = 0, Height = 0 });
        _source.AddHook(WndProc);
    }

    public event Action? Pressed;

    /// <summary>Teks status untuk Settings.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Mendaftarkan ulang hotkey; teks tidak valid atau bentrok → status peringatan.</summary>
    public void Register(string gestureText)
    {
        Unregister();
        if (!HotkeyGesture.TryParse(gestureText, out var gesture))
        {
            Status = "Invalid hotkey.";
            return;
        }

        _registered = RegisterHotKey(_source.Handle, HotkeyId, (uint)gesture.Modifiers | ModNoRepeat, gesture.VirtualKey);
        Status = _registered ? $"{gesture} is registered." : $"{gesture} is in use by another app. Choose another hotkey.";
        if (!_registered)
        {
            _log.Warn("Hotkey registration failed (in use by another app).");
        }
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
