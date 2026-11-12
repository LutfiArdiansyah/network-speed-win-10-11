using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NetworkSpeed.App.Composition;
using NetworkSpeed.Core.Display;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Core.Settings;
using NetworkSpeed.Core.Taskbar;
using NetworkSpeed.Interop.Shell;
using NetworkSpeed.Interop.Taskbar;
using ThemeMode = NetworkSpeed.Core.Settings.ThemeMode;

namespace NetworkSpeed.App.Overlay;

/// <summary>
/// Mengelola satu <see cref="OverlayWindow"/> per taskbar terpilih (spec E-03): menghitung ulang posisi saat taskbar
/// berubah (T-05, debounce 250 ms, scan di thread background), menyembunyikan saat fullscreen/auto-hide/tidak muat
/// (D-05, E-01, F-07b), dan menerapkan teks & warna. Semua anggota publik dipanggil dari UI thread.
/// </summary>
public sealed class OverlayCoordinator : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan NormalPoll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AutoHidePoll = TimeSpan.FromMilliseconds(200);

    private readonly AppHost _host;
    private readonly ITaskbarLayoutReader _reader;
    private readonly TaskbarChangeMonitor _monitor;
    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _pollTimer;
    private readonly Dictionary<string, OverlayWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Placement> _placements = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int Width, int Height)> _sizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _visible = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _toolTipOpen = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskbarForegroundWatcher _foreground = new();

    private IReadOnlyList<TaskbarLayout> _layouts = [];
    private IReadOnlyList<PixelRect> _lastRects = [];
    private OverlayBrushes _brushes;
    private bool _recalcInFlight;
    private bool _recalcPending;
    private bool _fullscreen;
    private bool _disposed;
    private int _detailRetries;

    // Scan UIA bisa gagal sesaat (Explorer/COM baru mulai); layout horizontal tanpa detail dicoba ulang tiap poll.
    private const int MaxDetailRetries = 10;

    public OverlayCoordinator(AppHost host, ITaskbarLayoutReader reader)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(reader);
        _host = host;
        _reader = reader;
        _monitor = new TaskbarChangeMonitor(reader, host.Log);
        _brushes = CreateBrushes(host.Settings);
        _debounceTimer = new DispatcherTimer { Interval = Debounce };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            StartRecalc();
        };
        _pollTimer = new DispatcherTimer { Interval = NormalPoll };
        _pollTimer.Tick += (_, _) => Poll();
    }

    public event Action? ContextMenuRequested;

    /// <summary>Dipicu setiap kali posisi/visibilitas overlay diterapkan (mis. untuk menyegarkan status di Settings).</summary>
    public event Action? PlacementsApplied;

    /// <summary>Overlay disembunyikan oleh pengguna (menu/hotkey).</summary>
    public bool IsUserHidden { get; private set; }

    /// <summary>Alasan overlay tidak tampil di taskbar utama, untuk Settings/tooltip; null bila tampil.</summary>
    public string? HiddenReason { get; private set; }

    /// <summary>Layout terakhir yang terbaca (untuk daftar posisi di Settings).</summary>
    public IReadOnlyList<TaskbarLayout> Layouts => _layouts;

    public void Start()
    {
        _host.ReadingUpdated += OnReading;
        _host.SettingsChanged += OnSettingsChanged;
        _host.Resumed += () => Request("resume");
        _monitor.Changed += reason => Application.Current?.Dispatcher.BeginInvoke(() => Request(reason));
        _monitor.Start();
        _foreground.TaskbarActivated += OnTaskbarActivated;
        _foreground.Start();
        _pollTimer.Start();
        StartRecalc();
    }

    /// <summary>
    /// Tooltip salah satu overlay akan tampil. Penerima boleh memanggil <see cref="SetToolTip"/> di sini untuk mengisi
    /// data yang hanya dibaca saat tooltip terlihat (SSID).
    /// </summary>
    public event Action? ToolTipOpening;

    /// <summary>Ada tooltip overlay yang sedang terbuka.</summary>
    public bool IsToolTipOpen => _toolTipOpen.Count > 0;

    /// <summary>Tooltip semua overlay (spec F-06).</summary>
    public void SetToolTip(string text)
    {
        foreach (var window in _windows.Values)
        {
            window.ToolTip = text;
        }
    }

    public void ToggleUserHidden()
    {
        IsUserHidden = !IsUserHidden;
        _host.Log.Info(IsUserHidden ? "Overlay hidden by user." : "Overlay shown by user.");
        ApplyPlacements();
    }

    public void Request(string reason)
    {
        _ = reason;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _debounceTimer.Stop();
        _pollTimer.Stop();
        _foreground.Dispose();
        _monitor.Dispose();
        foreach (var window in _windows.Values)
        {
            window.Close();
        }

        _windows.Clear();
    }

    private void StartRecalc()
    {
        if (_disposed)
        {
            return;
        }

        if (_recalcInFlight)
        {
            _recalcPending = true;
            return;
        }

        _recalcInFlight = true;
        Task.Run(_reader.ReadAll).ContinueWith(
            t =>
            {
                _recalcInFlight = false;
                if (_disposed)
                {
                    return;
                }

                if (t.IsFaulted)
                {
                    _host.Log.Error("Reading taskbar layout failed.", t.Exception?.InnerException);
                }
                else
                {
                    _layouts = t.Result;
                    _lastRects = _reader.ReadWin32Rects();
                    SyncWindows();
                    ApplyPlacements();
                }

                if (_recalcPending)
                {
                    _recalcPending = false;
                    Request("pending");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    // Satu jendela per taskbar terpilih; jendela milik monitor yang hilang ditutup.
    private void SyncWindows()
    {
        // Tidak ada taskbar sama sekali (Explorer sedang restart): jendela dipertahankan tetapi disembunyikan
        // lewat ApplyPlacements, agar penerima TaskbarCreated tetap ada dan overlay bisa kembali.
        if (_layouts.Count == 0)
        {
            return;
        }

        var selected = MonitorSelector.Select(_layouts, _host.Settings.MonitorMode, _host.Settings.SpecificMonitorId);
        var wanted = selected.Select(l => l.MonitorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _windows.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _windows[stale].Close();
            _windows.Remove(stale);
            _placements.Remove(stale);
            _toolTipOpen.Remove(stale);
        }

        foreach (var layout in selected)
        {
            if (_windows.ContainsKey(layout.MonitorId))
            {
                continue;
            }

            var window = new OverlayWindow { MonitorId = layout.MonitorId };
            window.ShellMessage += OnShellMessage;
            window.ContextMenuRequested += () => ContextMenuRequested?.Invoke();
            var monitorId = layout.MonitorId;
            window.ToolTipOpening += (_, _) =>
            {
                _toolTipOpen.Add(monitorId);
                ToolTipOpening?.Invoke();
            };
            window.ToolTipClosing += (_, _) => _toolTipOpen.Remove(monitorId);
            window.Show();

            // Pindahkan (masih tersembunyi) ke monitor tujuan sebelum diukur, agar DPI-nya sudah DPI monitor itu.
            window.MoveHiddenTo(layout.Taskbar.Left, layout.Taskbar.Top);
            ApplyStyle(window);
            _windows[layout.MonitorId] = window;
        }
    }

    private void ApplyPlacements()
    {
        var settings = _host.Settings;
        string? primaryReason = null;
        foreach (var (monitorId, window) in _windows)
        {
            var layout = _layouts.FirstOrDefault(l => string.Equals(l.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));
            var (width, height) = window.PixelSize();
            var placement = PlacementCalculator.Calculate(layout, settings.Position, width, height, settings.OffsetFor);
            var hiddenReason = !placement.IsVisible ? placement.Reason
                : IsUserHidden ? "Hidden by user"
                : _fullscreen ? "Hidden while a full-screen app is active"
                : null;

            if (hiddenReason is null)
            {
                window.PlaceAt(placement.X, placement.Y);
                _visible.Add(monitorId);
            }
            else
            {
                window.HideOverlay();
                _visible.Remove(monitorId);
            }

            if (layout?.IsPrimary == true || _windows.Count == 1)
            {
                primaryReason = hiddenReason;
            }

            _sizes[monitorId] = (width, height);
            LogIfChanged(monitorId, placement);
        }

        HiddenReason = _windows.Count == 0 || _layouts.Count == 0 ? PlacementCalculator.NotFoundReason : primaryReason;
        PlacementsApplied?.Invoke();
    }

    /// <summary>
    /// Taskbar yang baru diaktifkan (diklik) naik di atas jendela topmost lain; overlay dinaikkan lagi (spec T-09).
    /// </summary>
    private void OnTaskbarActivated()
    {
        foreach (var (monitorId, window) in _windows)
        {
            if (_visible.Contains(monitorId))
            {
                window.ReassertTopmost();
            }
        }
    }

    private void LogIfChanged(string monitorId, Placement placement)
    {
        if (_placements.TryGetValue(monitorId, out var previous) && previous == placement)
        {
            return;
        }

        _placements[monitorId] = placement;
        _host.Log.Info(string.Create(
            CultureInfo.InvariantCulture,
            $"Overlay placement monitor={monitorId} visible={placement.IsVisible} effective={placement.Effective} source={placement.Source} x={placement.X} y={placement.Y} reason={placement.Reason}"));
    }

    private void Poll()
    {
        if (_layouts.Any(l => !l.IsVertical && !l.HasUiaDetail))
        {
            if (_detailRetries < MaxDetailRetries)
            {
                _detailRetries++;
                StartRecalc();
            }
        }
        else
        {
            _detailRetries = 0;
        }

        var settings = _host.Settings;
        var fullscreen = settings.HideOnFullscreen && ShellState.IsFullscreenAppActive();
        if (fullscreen != _fullscreen)
        {
            _fullscreen = fullscreen;
            ApplyPlacements();
        }

        // Auto-hide (E-01): taskbar bergeser keluar/masuk layar; ikuti dengan cek rect Win32 yang murah.
        var autoHide = ShellState.IsTaskbarAutoHide();
        _pollTimer.Interval = autoHide ? AutoHidePoll : NormalPoll;
        if (autoHide)
        {
            var rects = _reader.ReadWin32Rects();
            if (!rects.SequenceEqual(_lastRects))
            {
                _lastRects = rects;
                StartRecalc();
            }
        }
    }

    private void OnShellMessage(int message)
    {
        if (message == OverlayWindow.TaskbarCreatedMessage)
        {
            _host.Log.Info("Taskbar recreated (Explorer restart).");
            _monitor.Resubscribe();
        }

        if (message == 0x001A)
        {
            _brushes = CreateBrushes(_host.Settings);
            foreach (var window in _windows.Values)
            {
                ApplyStyle(window);
            }
        }

        Request("shell-message");
    }

    private void OnSettingsChanged(UserSettings settings)
    {
        _brushes = CreateBrushes(settings);
        foreach (var window in _windows.Values)
        {
            ApplyStyle(window);
        }

        SyncWindows();
        Request("settings");
    }

    private void OnReading(SpeedReading? reading)
    {
        var text = OverlayTextBuilder.Build(reading, _host.Settings, LatestLatency);
        var settings = _host.Settings;
        var resized = false;
        foreach (var (monitorId, window) in _windows)
        {
            window.ShowText(text, _brushes, new FontFamily(settings.FontFamily), FontSizeInDip(settings.FontSize));
            resized |= _sizes.TryGetValue(monitorId, out var size) && size != window.PixelSize();
        }

        // Ukuran berubah (mis. "Offline", latency "timeout"): hitung ulang dari layout yang sudah ada (tanpa scan UIA),
        // agar overlay tetap di tengah vertikal dan tidak melebar ke area tray.
        if (resized)
        {
            ApplyPlacements();
        }
    }

    /// <summary>Latency terbaru untuk teks overlay (diisi fitur ping).</summary>
    public LatencyReading? LatestLatency { get; set; }

    private void ApplyStyle(OverlayWindow window)
    {
        var settings = _host.Settings;
        var font = new FontFamily(settings.FontFamily);
        var size = FontSizeInDip(settings.FontSize);
        window.StableWidth = MeasureStableWidth(window, settings, font, size);
        window.ShowText(OverlayTextBuilder.Build(_host.LastReading, settings, LatestLatency), _brushes, font, size);
    }

    // Lebar teks terlebar yang mungkin, supaya overlay tidak "melompat" saat angka berubah (F-02).
    private static double MeasureStableWidth(Visual visual, UserSettings settings, FontFamily font, double size)
    {
        var unit = settings.UnitMode == Core.Formatting.SpeedUnitMode.BitsPerSecond ? "Mbps" : "MB/s";
        var sample = settings.Layout == OverlayLayout.OneLine
            ? $"↑ 888 {unit}  ↓ 888 {unit}" + (settings.ShowLatencyOnOverlay ? "  888 ms" : string.Empty)
            : $"↓ 888 {unit}";
        var typeface = new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(sample, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, VisualTreeHelper.GetDpi(visual).PixelsPerDip);
        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
    }

    // FontSize pengaturan dalam point; WPF memakai DIP (1 pt = 96/72 DIP).
    private static double FontSizeInDip(double points) => points * 96 / 72;

    private static OverlayBrushes CreateBrushes(UserSettings s)
    {
        if (SystemParameters.HighContrast)
        {
            // D-06: mode kontras tinggi memakai warna sistem.
            var text = SystemColors.WindowTextBrush;
            return new OverlayBrushes(text, text, SystemColors.HighlightBrush, SystemColors.WindowBrush);
        }

        var light = s.Theme switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => ShellState.IsLightTheme(),
        };
        var themeText = light ? Brushes.Black : Brushes.White;
        var background = new SolidColorBrush(light ? Colors.White : Colors.Black)
        {
            // Alpha minimal 1 agar overlay tetap bisa diklik kanan.
            Opacity = Math.Max(s.BackgroundOpacity, 1.0 / 255),
        };
        background.Freeze();
        return new OverlayBrushes(ParseOr(s.UpColor, themeText), ParseOr(s.DownColor, themeText), ParseOr(s.AlertColor, Brushes.Red), background);
    }

    private static Brush ParseOr(string? color, Brush fallback)
    {
        if (color is null || ColorConverter.ConvertFromString(color) is not Color parsed)
        {
            return fallback;
        }

        var brush = new SolidColorBrush(parsed);
        brush.Freeze();
        return brush;
    }
}
