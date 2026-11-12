using System.Runtime.InteropServices;
using System.Windows.Automation;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Core.Taskbar;

namespace NetworkSpeed.Interop.Taskbar;

/// <summary>
/// Memberi tahu saat layout taskbar mungkin berubah (spec T-05): event UIA <c>StructureChanged</c>
/// (pin/unpin, app buka/tutup) dan polling Win32 murah atas rect taskbar/tray.
/// Event <see cref="Changed"/> dipicu dari thread pool; pemanggil wajib marshal ke UI thread dan men-debounce.
/// </summary>
public sealed class TaskbarChangeMonitor : IDisposable
{
    public const string UiaReason = "uia-structure";
    public const string RectReason = "win32-rect";

    /// <summary>Jendela taskbar baru terdeteksi (Explorer restart); langganan UIA sudah diperbarui.</summary>
    public const string RecreatedReason = "taskbar-recreated";

    private readonly ITaskbarLayoutReader _reader;
    private readonly IAppLog _log;
    private readonly TimeSpan _pollInterval;
    private readonly Lock _gate = new();
    private Timer? _timer;
    private IReadOnlyList<PixelRect> _lastRects = [];
    private AutomationElement? _subscribedRoot;
    private StructureChangedEventHandler? _handler;
    private bool _disposed;
    private int _subscribeFailures;
    private nint _lastTaskbar;

    private const int MaxSubscribeRetries = 6;
    private static readonly TimeSpan SubscribeRetryDelay = TimeSpan.FromSeconds(5);

    public TaskbarChangeMonitor(ITaskbarLayoutReader reader, IAppLog log, TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(log);
        _reader = reader;
        _log = log;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
    }

    public event Action<string>? Changed;

    public void Start()
    {
        _lastRects = _reader.ReadWin32Rects();
        _lastTaskbar = _reader.PrimaryTaskbarHandle();
        _timer = new Timer(_ => Poll(), null, _pollInterval, _pollInterval);
        Resubscribe();
    }

    /// <summary>Daftar ulang event UIA, mis. setelah <c>TaskbarCreated</c> (Explorer restart).</summary>
    public void Resubscribe()
    {
        lock (_gate)
        {
            _subscribeFailures = 0;
        }

        Task.Run(SubscribeUia);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            UnsubscribeUia();
        }

        _timer?.Dispose();
    }

    private void Poll()
    {
        // Explorer restart membuat jendela taskbar baru: elemen UIA lama mati, jadi langganan harus diperbarui.
        // Tidak bergantung pada broadcast TaskbarCreated, yang hanya diterima jendela top-level yang masih ada.
        var taskbar = _reader.PrimaryTaskbarHandle();
        if (taskbar != 0 && taskbar != _lastTaskbar)
        {
            var wasRunning = _lastTaskbar != 0;
            _lastTaskbar = taskbar;
            if (wasRunning)
            {
                _log.Info("Taskbar window replaced (Explorer restart); resubscribing.");
                Resubscribe();
                Changed?.Invoke(RecreatedReason);
                return;
            }
        }

        var rects = _reader.ReadWin32Rects();
        if (rects.SequenceEqual(_lastRects))
        {
            return;
        }

        _lastRects = rects;
        Changed?.Invoke(RectReason);
    }

    // Dijalankan di thread pool: operasi UIA tidak boleh di UI thread.
    private void SubscribeUia()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            UnsubscribeUia();
            var tray = WindowNative.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var root = AutomationElement.FromHandle(tray);
                _handler = (_, _) => Changed?.Invoke(UiaReason);
                Automation.AddStructureChangedEventHandler(root, TreeScope.Subtree, _handler);
                _subscribedRoot = root;
                _subscribeFailures = 0;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
            {
                // Polling Win32 tetap berjalan sebagai jaring pengaman; UIA sering gagal sesaat saat Explorer/COM baru mulai.
                _subscribeFailures++;
                var retry = _subscribeFailures <= MaxSubscribeRetries;
                _log.Warn($"Taskbar UIA subscription failed ({ex.GetType().Name}), attempt {_subscribeFailures}; {(retry ? "retrying" : "relying on polling")}.");
                if (retry)
                {
                    _ = Task.Delay(SubscribeRetryDelay).ContinueWith(_ => SubscribeUia(), TaskScheduler.Default);
                }
            }
        }
    }

    private void UnsubscribeUia()
    {
        if (_subscribedRoot is null || _handler is null)
        {
            return;
        }

        try
        {
            Automation.RemoveStructureChangedEventHandler(_subscribedRoot, _handler);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            // Elemen lama sudah hilang (Explorer restart); tidak ada yang perlu dilepas.
        }

        _subscribedRoot = null;
        _handler = null;
    }
}
