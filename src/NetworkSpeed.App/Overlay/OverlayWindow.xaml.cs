using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using NetworkSpeed.Core.Display;

namespace NetworkSpeed.App.Overlay;

/// <summary>Warna overlay hasil tema/pengaturan (spec D-01, D-02, D-04, D-06).</summary>
public sealed record OverlayBrushes(Brush Up, Brush Down, Brush Alert, Brush Background);

/// <summary>
/// Jendela overlay di atas taskbar (spec T-09): tanpa border, transparan, tidak muncul di Alt+Tab/taskbar,
/// tidak mencuri fokus, dan selalu di atas. Posisi diatur dalam piksel fisik lewat <c>SetWindowPos</c>.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExTopmost = 0x00000008;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpHideWindow = 0x0080;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private static readonly IntPtr HwndTopmost = new(-1);

    private IntPtr _hwnd;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        MouseRightButtonUp += (_, _) => ContextMenuRequested?.Invoke();
    }

    /// <summary>Pesan Windows yang relevan untuk posisi (TaskbarCreated, setting/display/DPI change).</summary>
    public event Action<int>? ShellMessage;

    public event Action? ContextMenuRequested;

    public static int TaskbarCreatedMessage { get; } = (int)RegisterWindowMessage("TaskbarCreated");

    public string MonitorId { get; set; } = string.Empty;

    /// <summary>Lebar teks minimum agar ukuran overlay stabil saat angka berubah.</summary>
    public double StableWidth { get; set; }

    public void ShowText(OverlayText text, OverlayBrushes brushes, FontFamily font, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(brushes);
        Root.Background = brushes.Background;
        Lines.MinWidth = StableWidth;
        while (Lines.Children.Count < text.Lines.Count)
        {
            Lines.Children.Add(new TextBlock { TextAlignment = TextAlignment.Left });
        }

        while (Lines.Children.Count > text.Lines.Count)
        {
            Lines.Children.RemoveAt(Lines.Children.Count - 1);
        }

        for (var i = 0; i < text.Lines.Count; i++)
        {
            var block = (TextBlock)Lines.Children[i];
            block.Text = text.Lines[i];
            block.FontFamily = font;
            block.FontSize = fontSize;
            block.Foreground = ForegroundFor(i, text, brushes);
        }

        AutomationProperties.SetName(this, string.Join(", ", text.Lines));
    }

    /// <summary>Ukuran jendela dalam piksel fisik (setelah layout).</summary>
    public (int Width, int Height) PixelSize()
    {
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        return ((int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
    }

    public void PlaceAt(int x, int y)
    {
        if (_hwnd != IntPtr.Zero)
        {
            SetWindowPos(_hwnd, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate | SwpShowWindow);
        }
    }

    /// <summary>Menaikkan lagi ke atas tanpa memindah/mengaktifkan (spec T-09).</summary>
    public void ReassertTopmost()
    {
        if (_hwnd != IntPtr.Zero)
        {
            SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
        }
    }

    /// <summary>Memindah jendela (tetap tersembunyi) ke monitor tujuan, agar DPI & ukurannya mengikuti monitor itu.</summary>
    public void MoveHiddenTo(int x, int y)
    {
        if (_hwnd != IntPtr.Zero)
        {
            SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpHideWindow);
        }
    }

    public void HideOverlay()
    {
        if (_hwnd != IntPtr.Zero)
        {
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpHideWindow);
        }
    }

    private static Brush ForegroundFor(int line, OverlayText text, OverlayBrushes b)
    {
        if (text.Lines.Count == 2)
        {
            return line == 0 ? (text.IsUpAlert ? b.Alert : b.Up) : (text.IsDownAlert ? b.Alert : b.Down);
        }

        return text.IsUpAlert || text.IsDownAlert ? b.Alert : b.Down;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(style | WsExToolWindow | WsExNoActivate | WsExTopmost));
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmSettingChange = 0x001A;
        const int WmDisplayChange = 0x007E;
        const int WmDpiChanged = 0x02E0;
        if (msg == TaskbarCreatedMessage || msg is WmSettingChange or WmDisplayChange or WmDpiChanged)
        {
            ShellMessage?.Invoke(msg);
        }

        return IntPtr.Zero;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"Overlay {MonitorId}");

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint RegisterWindowMessage(string message);
}
