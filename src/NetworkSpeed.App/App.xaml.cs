using System.IO;
using System.Windows;
using System.Windows.Threading;
using NetworkSpeed.App.Composition;
using NetworkSpeed.App.Features;
using NetworkSpeed.App.Notifications;
using NetworkSpeed.App.Overlay;
using NetworkSpeed.App.Platform;
using NetworkSpeed.App.Settings;
using NetworkSpeed.App.Tray;
using NetworkSpeed.App.Viewer;
using NetworkSpeed.Core.Display;
using NetworkSpeed.Core.Ipc;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Core.Settings;
using NetworkSpeed.Core.Startup;
using NetworkSpeed.Core.Storage;
using NetworkSpeed.Core.Viewer;
using NetworkSpeed.Interop.Taskbar;

namespace NetworkSpeed.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Siklus hidup WPF Application: resource dilepas di OnExit.")]
public partial class App : Application
{
    private RollingFileLog? _log;
    private SingleInstance? _instance;
    private AppHost? _host;
    private TrayIconController? _tray;
    private OverlayCoordinator? _overlay;
    private SettingsWindow? _settingsWindow;
    private GlobalHotkey? _hotkey;
    private string? _registeredHotkey;
    private LatencyMonitor? _latency;
    private BalloonNotifier? _balloons;
    private ServiceConnection? _service;
    private QuotaMonitor? _quota;
    private UpdateMonitor? _updates;
    private AppExitListener? _exitListener;
    private ViewerLauncher? _viewers;
    private ViewerSession? _viewerSession;
    private readonly LiveHistoryBuffer _live = new();
    private readonly AdapterDetailsCache _details = new();

    /// <summary>Folder data per user: <c>%LOCALAPPDATA%\NetworkSpeed</c> (spec §5, §7).</summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetworkSpeed");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (ViewerCommandLine.IsViewer(e.Args))
        {
            // Proses viewer (riwayat/per aplikasi) bercabang sebelum mutex single instance: tanpa overlay/tray/kuota.
            StartViewer(e.Args);
            return;
        }

        var log = new RollingFileLog(Path.Combine(DataDirectory, "logs"));
        HookUnhandledExceptions(log);

        _instance = SingleInstance.TryAcquire(() => Dispatcher.BeginInvoke(OnShowSettingsRequested));
        if (_instance is null)
        {
            SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        var (settings, canSave) = LoadSettings(log);
        if (AutostartGate.ShouldExit(e.Args, settings))
        {
            log.Info("Autostart disabled by user; exiting.");
            Shutdown();
            return;
        }

        var store = new SettingsFileStore(Path.Combine(DataDirectory, "settings.json"), TimeProvider.System);
        _host = new AppHost(log, store, settings, canSave);
        _tray = new TrayIconController(_host, () => Shutdown());
        _overlay = new OverlayCoordinator(_host, new TaskbarLayoutReader());
        _overlay.ContextMenuRequested += _tray.ShowMenuAtCursor;
        _tray.ShowSettingsAction = ShowSettingsWindow;
        _tray.AddMenuItem("Settings…", ShowSettingsWindow);
        _tray.AddMenuItem("Show/Hide overlay", _overlay.ToggleUserHidden);
        _viewers = new ViewerLauncher(log);
        var viewers = _viewers;
        _tray.AddMenuItem("History…", () => viewers.Show(ViewerKind.History, _live.Snapshot()));
        _tray.AddMenuItem("Per-app usage…", () => viewers.Show(ViewerKind.PerApp, []));

        _service = new ServiceConnection(new ServiceClientTransport(new ServiceClient()), new ReconnectBackoff());
        _service.SubscriberFailed += ex => log.Error("Service connection handler failed.", ex);
        _exitListener = new AppExitListener(
            () => Interop.Lifecycle.GlobalEventNative.TryOpenForWait(Core.Lifecycle.AppExitSignal.Name),
            () => Dispatcher.BeginInvoke(() =>
            {
                log.Info("Exit requested by installer.");
                Shutdown();
            }));
        var exitListener = _exitListener;
        _service.StateChanged += () => exitListener.TryAttach();
        exitListener.TryAttach();
        // Satu instance untuk kuota & update: store menulis seluruh isi file, dua instance akan saling menimpa kunci.
        var notificationState = new NotificationStateStore(Path.Combine(DataDirectory, "state.json"), TimeProvider.System);
        _quota = new QuotaMonitor(_host, _service, notificationState, _tray);
        var host = _host;
        var tray = _tray;
        _updates = new UpdateMonitor(
            new FileManifestReader(),
            MachineSettings.UpdateFolder,
            () => host.Settings.CheckForUpdates,
            typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0),
            notificationState,
            tray.ShowBalloon,
            log);
        _updates.StatusChanged += () => _settingsWindow?.RefreshStatus();
        _host.ReadingUpdated += reading => _live.Add(reading?.UpBytesPerSecond ?? 0, reading?.DownBytesPerSecond ?? 0);

        var overlay = _overlay;
        _hotkey = new GlobalHotkey(log);
        _hotkey.Pressed += overlay.ToggleUserHidden;
        RegisterHotkeyIfChanged(settings.Hotkey);
        _latency = new LatencyMonitor(_host);
        _latency.Updated += latency => overlay.LatestLatency = latency;
        _balloons = new BalloonNotifier(_host, _tray);
        overlay.PlacementsApplied += () => _settingsWindow?.RefreshStatus();
        _host.SettingsChanged += s =>
        {
            RegisterHotkeyIfChanged(s.Hotkey);
            _settingsWindow?.RefreshStatus();
        };
        _host.ReadingUpdated += UpdateToolTips;

        // SSID baru dibaca saat tooltip akan tampil (bukan tiap 30 detik di background) agar icon lokasi Windows
        // tidak terus muncul; isi tooltip diperbarui sebelum tampil.
        overlay.ToolTipOpening += () => UpdateToolTips(_host.LastReading);

        _host.Start();
        _overlay.Start();
        _latency.Start();
        _service.Start();
        _quota.Start();
        _updates.Start();
        log.Info("App started.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewerSession?.Dispose();
        _viewers?.Dispose();
        _exitListener?.Dispose();
        _updates?.Dispose();
        _quota?.Dispose();
        _service?.Dispose();
        _balloons?.Dispose();
        _latency?.Dispose();
        _hotkey?.Dispose();
        _overlay?.Dispose();
        _tray?.Dispose();
        _host?.Dispose();
        if (_instance is not null)
        {
            // Hanya instance utama yang mencatat; instance kedua keluar tanpa menulis log.
            _instance.Dispose();
            _log?.Info("App exited.");
        }

        base.OnExit(e);
    }

    private static (UserSettings Settings, bool CanSave) LoadSettings(RollingFileLog log)
    {
        var store = new SettingsFileStore(Path.Combine(DataDirectory, "settings.json"), TimeProvider.System);
        try
        {
            var (settings, outcome) = store.Load();
            if (outcome == SettingsLoadOutcome.CorruptRecovered)
            {
                log.Warn("Settings file was corrupt; defaults restored and old file kept.");
            }

            if (outcome != SettingsLoadOutcome.Loaded)
            {
                // Tulis default agar file ada dan bisa ditemukan/diedit pengguna.
                store.Save(settings);
            }

            return (settings, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File ada tetapi tidak bisa dibaca: pakai default dan jangan menimpa file pengguna.
            log.Error("Settings file could not be read; using defaults without saving.", ex);
            return (new UserSettings(), false);
        }
    }

    private string HotkeyStatus => _hotkey?.Status ?? string.Empty;

    // Daftar ulang hanya bila teks berubah: slider di Settings memicu SettingsChanged berkali-kali.
    private void RegisterHotkeyIfChanged(string hotkey)
    {
        if (_hotkey is null || string.Equals(hotkey, _registeredHotkey, StringComparison.Ordinal))
        {
            return;
        }

        _registeredHotkey = hotkey;
        _hotkey.Register(hotkey);
    }

    private void UpdateToolTips(SpeedReading? reading)
    {
        if (_overlay is null || _tray is null)
        {
            return;
        }

        var adapter = reading is { SelectedAdapters.Count: > 0 } ? reading.SelectedAdapters[0] : null;
        var (ssid, ipv4) = adapter is null ? (null, null) : _details.Get(adapter, includeSsid: _overlay.IsToolTipOpen);
        var name = reading is { SelectedAdapters.Count: > 1 } ? string.Join(", ", reading.SelectedAdapters.Select(a => a.Name)) : adapter?.Name;
        var data = new TooltipData(name, ssid, ipv4, reading?.SessionSentBytes ?? 0, reading?.SessionReceivedBytes ?? 0, _latency?.Latest, _overlay.HiddenReason);
        var quota = _quota?.Summary;
        _overlay.SetToolTip(TooltipBuilder.Build(data) + (string.IsNullOrEmpty(quota) ? string.Empty : Environment.NewLine + quota));
        var text = OverlayTextBuilder.Build(reading, _host!.Settings with { Layout = OverlayLayout.OneLine, ShowLatencyOnOverlay = false }, null);
        _tray.SetToolTip("Network Speed" + Environment.NewLine + text.Lines[0]);
    }

    private void ShowSettingsWindow()
    {
        if (_host is null || _overlay is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            var overlay = _overlay;
            var updates = _updates;
            _settingsWindow = new SettingsWindow(
                _host,
                () => overlay.Layouts,
                () => overlay.HiddenReason,
                () => HotkeyStatus,
                Core.Update.UpdateFolderPath.Normalize(MachineSettings.UpdateFolder()),
                () => updates?.StatusText ?? string.Empty);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
    }

    private void StartViewer(string[] args)
    {
        if (ViewerCommandLine.Parse(args) is not { } arguments)
        {
            Shutdown();
            return;
        }

        var log = new RollingFileLog(Path.Combine(DataDirectory, "logs"), baseName: ViewerSession.LogBaseName(arguments.Kind));
        HookUnhandledExceptions(log);
        try
        {
            _viewerSession = ViewerSession.Start(this, arguments, log, DataDirectory);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Tanpa ini exception ditelan handler dispatcher dan viewer hidup tanpa jendela: App utama mengira viewer
            // masih terbuka sehingga menu History…/Per-app usage… tidak berbuat apa-apa sampai App di-restart.
            log.Error("Viewer could not be opened.", ex);
            Shutdown();
        }
    }

    private void HookUnhandledExceptions(RollingFileLog log)
    {
        _log = log;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    private void OnShowSettingsRequested()
    {
        _log?.Info("Show settings requested by second instance.");
        _tray?.ShowSettings();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }
}
