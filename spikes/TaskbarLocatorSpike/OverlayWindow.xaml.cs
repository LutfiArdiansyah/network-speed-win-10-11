using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Formatting;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Core.Time;
using NetworkSpeed.Interop;
using static TaskbarLocatorSpike.NativeMethods;

namespace TaskbarLocatorSpike;

public partial class OverlayWindow : Window
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SafetyPoll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AutoHidePoll = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan AdapterRefresh = TimeSpan.FromSeconds(10);

    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _speedTimer;
    private readonly IfTableCounterSource _counters = new();
    private readonly SpeedCalculator _calculator = new();
    private readonly StopwatchClock _clock = new();

    private OverlayPosition _position;
    private IntPtr _hwnd;
    private uint _taskbarCreatedMsg;
    private int _locateInFlight;
    private string _pendingTrigger = "init";
    private int _structureEvents;
    private AutomationElement? _subscribedRoot;
    private StructureChangedEventHandler? _structureHandler;
    private IReadOnlyList<AdapterInfo> _adapters = [];
    private TimeSpan? _adaptersReadAt;
    private LocateResult? _last;
    private bool _hiddenBySystem;

    public OverlayWindow(OverlayPosition position)
    {
        _position = position;
        InitializeComponent();
        _debounceTimer = new DispatcherTimer { Interval = Debounce };
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); StartLocate(_pendingTrigger); };
        _pollTimer = new DispatcherTimer { Interval = SafetyPoll };
        _pollTimer.Tick += (_, _) => OnPoll();
        _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _speedTimer.Tick += (_, _) => UpdateSpeed();
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => UnsubscribeUia();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST));
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        ApplyTheme();
        SpikeLog.Write($"START position={_position} os={Environment.OSVersion.Version} log={SpikeLog.PathForDisplay}");
        SubscribeUia();
        _calculator.AddSnapshot(_counters.ReadCounters(), _clock.Elapsed);
        _speedTimer.Start();
        _pollTimer.Start();
        Request("init");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0)
        {
            SpikeLog.Write("EVENT TaskbarCreated (Explorer restart)");
            SubscribeUia();
            Request("TaskbarCreated");
        }
        else if (msg == WM_SETTINGCHANGE)
        {
            ApplyTheme();
            Request("WM_SETTINGCHANGE");
        }
        else if (msg == WM_DISPLAYCHANGE)
        {
            Request("WM_DISPLAYCHANGE");
        }
        else if (msg == WM_DPICHANGED)
        {
            Request("WM_DPICHANGED");
        }
        else if (msg == WM_POWERBROADCAST && wParam.ToInt32() == PBT_APMRESUMEAUTOMATIC)
        {
            _calculator.ResetBaseline();
            Request("resume");
        }

        return IntPtr.Zero;
    }

    private void Request(string trigger)
    {
        _pendingTrigger = trigger;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void StartLocate(string trigger)
    {
        if (Interlocked.Exchange(ref _locateInFlight, 1) == 1)
        {
            Request(trigger);
            return;
        }

        GetWindowRect(_hwnd, out var self);
        var position = _position;
        Task.Run(() => TaskbarLocator.Locate(position, self.Width, self.Height))
            .ContinueWith(t =>
            {
                Interlocked.Exchange(ref _locateInFlight, 0);
                if (t.IsFaulted)
                {
                    SpikeLog.Write($"LOCATE-ERROR trigger={trigger} {t.Exception!.InnerException?.GetType().Name}");
                    return;
                }

                Apply(t.Result, trigger);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Apply(LocateResult r, string trigger)
    {
        var changed = _last is null || _last.X != r.X || _last.Y != r.Y || _last.Visible != r.Visible || _last.Source != r.Source;
        _last = r;
        if (r.Visible && !_hiddenBySystem)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, r.X, r.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        Visibility = r.Visible && !_hiddenBySystem ? Visibility.Visible : Visibility.Hidden;
        ToolTip = $"req={r.Requested} eff={r.Effective} src={r.Source} {r.ElapsedMs:F1}ms\n{r.Detail}";
        if (changed || trigger != "poll")
        {
            SpikeLog.Write($"LOCATE trigger={trigger} req={r.Requested} eff={r.Effective} src={r.Source} x={r.X} y={r.Y} ms={r.ElapsedMs:F1} uiaEvents={_structureEvents} detail={r.Detail}");
        }
    }

    private void OnPoll()
    {
        var abd = new APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>() };
        var autoHide = ((int)SHAppBarMessage(ABM_GETSTATE, ref abd).ToUInt32() & ABS_AUTOHIDE) != 0;
        _pollTimer.Interval = autoHide ? AutoHidePoll : SafetyPoll;

        var hideReason = "";
        if (SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4)
        {
            hideReason = $"fullscreen(QUNS={state})";
        }
        else if (autoHide)
        {
            var hTray = FindWindow("Shell_TrayWnd", null);
            if (GetWindowRect(hTray, out var tb))
            {
                var screenBottom = (int)(SystemParameters.PrimaryScreenHeight * VisualTreeHelper.GetDpi(this).DpiScaleY);
                if (tb.Top >= screenBottom - 4)
                {
                    hideReason = "autohide";
                }
            }
        }

        var shouldHide = hideReason.Length > 0;
        if (shouldHide != _hiddenBySystem)
        {
            _hiddenBySystem = shouldHide;
            SpikeLog.Write(shouldHide ? $"HIDE {hideReason}" : "SHOW");
            Request("visibility");
            return;
        }

        // Jaring pengaman (T-05): hitung ulang tiap poll; saat auto-hide interval poll 200 ms.
        StartLocate("poll");
    }

    private void SubscribeUia()
    {
        var hTray = FindWindow("Shell_TrayWnd", null);
        Task.Run(() =>
        {
            try
            {
                UnsubscribeUia();
                if (hTray == IntPtr.Zero) return;
                var root = AutomationElement.FromHandle(hTray);
                _structureHandler = (_, _) =>
                {
                    Interlocked.Increment(ref _structureEvents);
                    Dispatcher.BeginInvoke(() => Request("uia-structure"));
                };
                Automation.AddStructureChangedEventHandler(root, TreeScope.Subtree, _structureHandler);
                _subscribedRoot = root;
                SpikeLog.Write("UIA subscribed StructureChanged on Shell_TrayWnd subtree");
            }
            catch (Exception ex)
            {
                SpikeLog.Write($"UIA subscribe failed: {ex.GetType().Name}");
            }
        });
    }

    private void UnsubscribeUia()
    {
        try
        {
            if (_subscribedRoot is not null && _structureHandler is not null)
            {
                Automation.RemoveStructureChangedEventHandler(_subscribedRoot, _structureHandler);
            }
        }
        catch (Exception ex)
        {
            SpikeLog.Write($"UIA unsubscribe failed: {ex.GetType().Name}");
        }

        _subscribedRoot = null;
    }

    private void UpdateSpeed()
    {
        var now = _clock.Elapsed;
        if (_adaptersReadAt is not { } readAt || now - readAt > AdapterRefresh)
        {
            _adapters = _counters.ReadAdapters();
            _adaptersReadAt = now;
        }

        var selected = AdapterSelector.Select(_adapters, AdapterSelectionMode.Auto, null, includeVpnInAll: false);
        var rates = _calculator.AddSnapshot(_counters.ReadCounters(), now);
        if (selected.Count == 0)
        {
            UpText.Text = "Offline";
            DownText.Text = "";
            return;
        }

        if (rates is null) return;
        var up = selected.Sum(id => rates.TryGetValue(id, out var r) ? r.SentBytesPerSecond : 0);
        var down = selected.Sum(id => rates.TryGetValue(id, out var r) ? r.ReceivedBytesPerSecond : 0);
        UpText.Text = "↑ " + SpeedFormatter.Format(up, SpeedUnitMode.BytesPerSecond);
        DownText.Text = "↓ " + SpeedFormatter.Format(down, SpeedUnitMode.BytesPerSecond);
    }

    private void ApplyTheme()
    {
        var light = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "SystemUsesLightTheme", 0) is int v && v == 1;
        var brush = light ? Brushes.Black : Brushes.White;
        UpText.Foreground = brush;
        DownText.Foreground = brush;
    }

    private void SetPosition(OverlayPosition p)
    {
        _position = p;
        SpikeLog.Write($"USER position={p}");
        Request("user");
    }

    private void OnPositionLeft(object sender, RoutedEventArgs e) => SetPosition(OverlayPosition.Left);

    private void OnPositionCenter(object sender, RoutedEventArgs e) => SetPosition(OverlayPosition.Center);

    private void OnPositionRight(object sender, RoutedEventArgs e) => SetPosition(OverlayPosition.Right);

    private void OnRecalc(object sender, RoutedEventArgs e) => Request("manual");

    private void OnOpenLog(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", Path.GetDirectoryName(SpikeLog.PathForDisplay)!) { UseShellExecute = true });

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
