using System.Diagnostics;
using System.Windows.Automation;
using static TaskbarLocatorSpike.NativeMethods;

namespace TaskbarLocatorSpike;

public enum OverlayPosition { Left, Center, Right }

/// <summary>Hasil satu kali perhitungan posisi (koordinat fisik/piksel layar).</summary>
public sealed record LocateResult(
    bool Visible, int X, int Y, OverlayPosition Requested, OverlayPosition? Effective,
    string Source, string Detail, double ElapsedMs);

/// <summary>Snapshot layout taskbar yang dibaca dari Win32 + UI Automation.</summary>
internal sealed record TaskbarLayout(
    RECT Taskbar, RECT? Notify, bool IsHorizontal, bool IsWin11Frame, bool IsCentered,
    double? StartLeft, double? LeftmostLeft, double? WidgetsRight, double? LastAppRight, int AppCount, double? UiaTrayLeft);

public static class TaskbarLocator
{
    private const int Margin = 8;
    private const string Win11AppButtonClass = "Taskbar.TaskListButtonAutomationPeer";

    public static LocateResult Locate(OverlayPosition requested, int width, int height)
    {
        var sw = Stopwatch.StartNew();
        var hTray = FindWindow("Shell_TrayWnd", null);
        if (hTray == IntPtr.Zero || !GetWindowRect(hTray, out var tb))
        {
            return new(false, 0, 0, requested, null, "none", "Shell_TrayWnd not found", sw.Elapsed.TotalMilliseconds);
        }

        RECT? notify = null;
        var hNotify = FindWindowEx(hTray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (hNotify != IntPtr.Zero && GetWindowRect(hNotify, out var nr)) notify = nr;

        TaskbarLayout? layout = null;
        var uiaError = "";
        try
        {
            layout = ReadLayout(hTray, tb, notify);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            uiaError = $"UIA failed: {ex.GetType().Name}; ";
        }

        if (layout is not null)
        {
            var candidates = requested == OverlayPosition.Right
                ? new[] { OverlayPosition.Right }
                : new[] { requested, OverlayPosition.Right };
            foreach (var candidate in candidates)
            {
                if (TryPlace(candidate, layout, width, height, out var x, out var y, out var why))
                {
                    var src = candidate == requested ? "uia" : "uia-fallback-right";
                    return new(true, x, y, requested, candidate, src, uiaError + why, sw.Elapsed.TotalMilliseconds);
                }

                uiaError += $"{candidate}: {why}; ";
            }
        }

        // Fallback terakhir (F-07b): koordinat Win32 TrayNotifyWnd tanpa UIA.
        if (notify is { } n && tb.Width > tb.Height)
        {
            var x = n.Left - width - Margin;
            var y = tb.Top + (tb.Height - height) / 2;
            return new(true, x, y, requested, OverlayPosition.Right, "win32-fallback", uiaError, sw.Elapsed.TotalMilliseconds);
        }

        return new(false, 0, 0, requested, null, "none", uiaError + "no horizontal taskbar/notify area", sw.Elapsed.TotalMilliseconds);
    }

    private static TaskbarLayout ReadLayout(IntPtr hTray, RECT tb, RECT? notify)
    {
        var isHorizontal = tb.Width > tb.Height;
        var root = AutomationElement.FromHandle(hTray);
        var frame = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "TaskbarFrame"));

        double? trayLeft = null;
        var allButtons = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        foreach (AutomationElement b in allButtons)
        {
            var c = b.Current;
            if (c.ClassName.StartsWith("SystemTray.", StringComparison.Ordinal) && !c.BoundingRectangle.IsEmpty)
            {
                trayLeft = Math.Min(trayLeft ?? double.MaxValue, c.BoundingRectangle.Left);
            }
        }

        if (frame is not null)
        {
            // Windows 11: semua tombol (Widgets, Start, Search, Task View, app) adalah anak TaskbarFrame.
            double? startLeft = null, leftmost = null, lastAppRight = null, widgetsRight = null;
            var appCount = 0;
            var children = frame.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement child in children)
            {
                var c = child.Current;
                if (c.BoundingRectangle.IsEmpty || c.IsOffscreen) continue;
                var r = c.BoundingRectangle;
                leftmost = Math.Min(leftmost ?? double.MaxValue, r.Left);
                if (c.AutomationId == "StartButton") startLeft = r.Left;
                if (c.ClassName == Win11AppButtonClass)
                {
                    appCount++;
                    lastAppRight = Math.Max(lastAppRight ?? 0, r.Right);
                }
            }

            // Pada taskbar rata tengah, tombol yang jauh di kiri Start adalah Widgets.
            foreach (AutomationElement child in children)
            {
                var c = child.Current;
                if (startLeft is { } s && !c.BoundingRectangle.IsEmpty && c.BoundingRectangle.Right <= s - 50)
                {
                    widgetsRight = Math.Max(widgetsRight ?? 0, c.BoundingRectangle.Right);
                }
            }

            var centered = startLeft is { } sl && sl > tb.Left + 150;
            return new(tb, notify, isHorizontal, true, centered, startLeft, leftmost, widgetsRight, lastAppRight, appCount, trayLeft);
        }

        // Windows 10: tombol app adalah anak MSTaskListWClass di dalam ReBarWindow32/MSTaskSwWClass.
        var rebar = FindWindowEx(hTray, IntPtr.Zero, "ReBarWindow32", null);
        var taskSw = rebar == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
        var list = taskSw == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(taskSw, IntPtr.Zero, "MSTaskListWClass", null);
        double? last = null;
        var count = 0;
        if (list != IntPtr.Zero)
        {
            var buttons = AutomationElement.FromHandle(list).FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement b in buttons)
            {
                var r = b.Current.BoundingRectangle;
                if (r.IsEmpty) continue;
                count++;
                last = Math.Max(last ?? 0, isHorizontal ? r.Right : r.Bottom);
            }
        }

        var hStart = FindWindowEx(hTray, IntPtr.Zero, "Start", null);
        double? win10Start = hStart != IntPtr.Zero && GetWindowRect(hStart, out var sr) ? sr.Left : null;
        return new(tb, notify, isHorizontal, false, false, win10Start, win10Start, null, last, count, trayLeft);
    }

    private static bool TryPlace(OverlayPosition pos, TaskbarLayout l, int w, int h, out int x, out int y, out string why)
    {
        x = y = 0;
        if (!l.IsHorizontal)
        {
            why = "vertical taskbar not supported in spike";
            return false;
        }

        y = l.Taskbar.Top + (l.Taskbar.Height - h) / 2;
        var trayLeft = (int)(l.UiaTrayLeft ?? l.Notify?.Left ?? l.Taskbar.Right);
        switch (pos)
        {
            case OverlayPosition.Right:
                x = trayLeft - w - Margin;
                if (l.LastAppRight is { } lar && x < lar + Margin)
                {
                    why = $"no room right (need {w}px, free {trayLeft - (int)lar - 2 * Margin}px)";
                    return false;
                }

                why = $"trayLeft={trayLeft}";
                return true;

            case OverlayPosition.Center:
                if (l.LastAppRight is not { } last)
                {
                    why = "no app buttons found";
                    return false;
                }

                x = (int)last + Margin;
                if (x + w + Margin > trayLeft)
                {
                    why = $"no room after last app (free {trayLeft - (int)last - 2 * Margin}px)";
                    return false;
                }

                why = $"lastAppRight={last} apps={l.AppCount}";
                return true;

            case OverlayPosition.Left:
                if (!l.IsWin11Frame || !l.IsCentered || l.LeftmostLeft is not { } leftmost)
                {
                    why = "left needs Win11 centered taskbar (left corner occupied by Start/apps)";
                    return false;
                }

                var leftEdge = (int)(l.WidgetsRight ?? l.Taskbar.Left);
                x = leftEdge + Margin;
                var limit = (int)(l.WidgetsRight is null ? leftmost : l.StartLeft ?? leftmost);
                if (x + w + Margin > limit)
                {
                    why = $"no room left (free {limit - leftEdge - 2 * Margin}px)";
                    return false;
                }

                why = $"leftEdge={leftEdge} widgets={(l.WidgetsRight is null ? "no" : "yes")}";
                return true;
        }

        why = "unknown";
        return false;
    }
}
