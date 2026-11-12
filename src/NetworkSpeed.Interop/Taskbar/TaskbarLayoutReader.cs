using System.Runtime.InteropServices;
using System.Windows.Automation;
using NetworkSpeed.Core.Taskbar;

namespace NetworkSpeed.Interop.Taskbar;

/// <summary>
/// Membaca layout semua taskbar (utama + monitor lain) dari Win32 dan UI Automation (spec F-07d).
/// Tidak membaca atau mengembalikan nama/AutomationId tombol app karena bisa memuat path pengguna.
/// Dipanggil dari thread background: scan UIA bisa memakan puluhan milidetik.
/// </summary>
public sealed class TaskbarLayoutReader : ITaskbarLayoutReader
{
    private const string PrimaryClass = "Shell_TrayWnd";
    private const string SecondaryClass = "Shell_SecondaryTrayWnd";
    private const string NotifyClass = "TrayNotifyWnd";
    private const string TaskbarFrameId = "TaskbarFrame";
    private const string StartButtonId = "StartButton";
    private const string Win11AppButtonClass = "Taskbar.TaskListButtonAutomationPeer";
    private const string TrayButtonClassPrefix = "SystemTray.";
    private const int Windows11Build = 22000;

    // Tombol yang ujung kanannya sejauh ini di kiri Start dianggap Widgets (taskbar Win11 rata tengah).
    private const int WidgetsGap = 50;

    // Start yang berada lebih dari ini dari tepi kiri berarti taskbar rata tengah.
    private const int CenteredThreshold = 150;

    private static readonly bool IsWindows11 = Environment.OSVersion.Version.Build >= Windows11Build;

    public IReadOnlyList<TaskbarLayout> ReadAll()
    {
        var layouts = new List<TaskbarLayout>();
        foreach (var (hwnd, isPrimary) in EnumerateTaskbars())
        {
            if (Read(hwnd, isPrimary) is { } layout)
            {
                layouts.Add(layout);
            }
        }

        return layouts;
    }

    /// <summary>Rect Win32 semua taskbar dan area tray; murah, untuk polling deteksi perubahan (spec T-05).</summary>
    public IReadOnlyList<PixelRect> ReadWin32Rects()
    {
        var rects = new List<PixelRect>();
        foreach (var (hwnd, _) in EnumerateTaskbars())
        {
            if (WindowNative.GetRect(hwnd) is { } r)
            {
                rects.Add(r);
            }

            if (WindowNative.GetRect(WindowNative.FindWindowEx(hwnd, IntPtr.Zero, NotifyClass, null)) is { } tray)
            {
                rects.Add(tray);
            }
        }

        return rects;
    }

    public nint PrimaryTaskbarHandle() => WindowNative.FindWindow(PrimaryClass, null);

    private static IEnumerable<(IntPtr Hwnd, bool IsPrimary)> EnumerateTaskbars()
    {
        var primary = WindowNative.FindWindow(PrimaryClass, null);
        if (primary != IntPtr.Zero)
        {
            yield return (primary, true);
        }

        var secondary = IntPtr.Zero;
        while ((secondary = WindowNative.FindWindowEx(IntPtr.Zero, secondary, SecondaryClass, null)) != IntPtr.Zero)
        {
            yield return (secondary, false);
        }
    }

    private static TaskbarLayout? Read(IntPtr hwnd, bool isPrimary)
    {
        if (WindowNative.GetRect(hwnd) is not { } taskbar || WindowNative.GetMonitor(hwnd) is not { } monitor)
        {
            return null;
        }

        var edge = EdgeOf(taskbar, monitor.Bounds);
        var notifyLeft = WindowNative.GetRect(WindowNative.FindWindowEx(hwnd, IntPtr.Zero, NotifyClass, null))?.Left;
        var win32Only = new TaskbarLayout(
            monitor.DeviceName, isPrimary, taskbar, edge, IsWindows11, IsCentered: false, HasUiaDetail: false,
            WidgetsRight: null, FirstButtonLeft: null, LastAppRight: null, TrayLeft: notifyLeft ?? taskbar.Right);

        if (edge is TaskbarEdge.Left or TaskbarEdge.Right)
        {
            return win32Only;
        }

        try
        {
            return ReadUiaDetail(hwnd, win32Only) ?? win32Only;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            // F-07b: UIA gagal (mis. Explorer sedang restart) → hanya data Win32.
            return win32Only;
        }
    }

    private static TaskbarLayout? ReadUiaDetail(IntPtr hwnd, TaskbarLayout win32Only)
    {
        var root = AutomationElement.FromHandle(hwnd);
        var trayLeft = Math.Min(win32Only.TrayLeft, UiaTrayLeft(root) ?? int.MaxValue);
        var frame = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, TaskbarFrameId));
        return frame is not null
            ? ReadWindows11(frame, win32Only with { TrayLeft = trayLeft })
            : ReadWindows10(hwnd, win32Only with { TrayLeft = trayLeft });
    }

    private static TaskbarLayout ReadWindows11(AutomationElement frame, TaskbarLayout layout)
    {
        var buttons = new List<(System.Windows.Rect Bounds, bool IsStart, bool IsApp)>();
        foreach (AutomationElement child in frame.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            var c = child.Current;
            if (c.IsOffscreen || c.BoundingRectangle.IsEmpty)
            {
                continue;
            }

            buttons.Add((c.BoundingRectangle, c.AutomationId == StartButtonId, c.ClassName == Win11AppButtonClass));
        }

        var start = buttons.FirstOrDefault(b => b.IsStart);
        double? startLeft = start.IsStart ? start.Bounds.Left : null;
        var widgets = startLeft is { } s ? buttons.Where(b => b.Bounds.Right <= s - WidgetsGap).ToList() : [];
        var centerGroup = buttons.Except(widgets).ToList();
        var apps = buttons.Where(b => b.IsApp).ToList();

        return layout with
        {
            IsCentered = startLeft is { } sl && sl > layout.Taskbar.Left + CenteredThreshold,
            HasUiaDetail = true,
            WidgetsRight = widgets.Count > 0 ? (int)widgets.Max(b => b.Bounds.Right) : null,
            FirstButtonLeft = centerGroup.Count > 0 ? (int)centerGroup.Min(b => b.Bounds.Left) : null,
            LastAppRight = apps.Count > 0 ? (int)apps.Max(b => b.Bounds.Right) : (centerGroup.Count > 0 ? (int)centerGroup.Max(b => b.Bounds.Right) : null),
        };
    }

    private static TaskbarLayout? ReadWindows10(IntPtr hwnd, TaskbarLayout layout)
    {
        var rebar = WindowNative.FindWindowEx(hwnd, IntPtr.Zero, "ReBarWindow32", null);
        var taskSwitch = rebar == IntPtr.Zero ? IntPtr.Zero : WindowNative.FindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
        var list = taskSwitch == IntPtr.Zero ? IntPtr.Zero : WindowNative.FindWindowEx(taskSwitch, IntPtr.Zero, "MSTaskListWClass", null);
        if (list == IntPtr.Zero)
        {
            return null;
        }

        var buttons = AutomationElement.FromHandle(list)
            .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
            .Cast<AutomationElement>()
            .Select(b => b.Current.BoundingRectangle)
            .Where(r => !r.IsEmpty)
            .ToList();
        var start = WindowNative.GetRect(WindowNative.FindWindowEx(hwnd, IntPtr.Zero, "Start", null));
        return layout with
        {
            HasUiaDetail = true,
            FirstButtonLeft = start?.Left ?? layout.Taskbar.Left,
            LastAppRight = buttons.Count > 0 ? (int)buttons.Max(r => r.Right) : WindowNative.GetRect(list)?.Left,
        };
    }

    private static int? UiaTrayLeft(AutomationElement root)
    {
        int? left = null;
        foreach (AutomationElement child in root.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
        {
            var c = child.Current;
            if (c.ClassName.StartsWith(TrayButtonClassPrefix, StringComparison.Ordinal) && !c.BoundingRectangle.IsEmpty)
            {
                left = Math.Min(left ?? int.MaxValue, (int)c.BoundingRectangle.Left);
            }
        }

        return left;
    }

    private static TaskbarEdge EdgeOf(PixelRect taskbar, PixelRect monitor)
    {
        if (taskbar.Width >= taskbar.Height)
        {
            return taskbar.Top <= monitor.Top ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        }

        return taskbar.Left <= monitor.Left ? TaskbarEdge.Left : TaskbarEdge.Right;
    }
}
