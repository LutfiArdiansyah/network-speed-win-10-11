using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using NetworkSpeed.App.Composition;
using NetworkSpeed.App.Features;
using NetworkSpeed.App.History;
using NetworkSpeed.App.PerApp;
using NetworkSpeed.Core.Display;
using NetworkSpeed.Core.Ipc;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Core.Settings;
using NetworkSpeed.Core.Storage;
using NetworkSpeed.Core.Viewer;

namespace NetworkSpeed.App.Viewer;

/// <summary>
/// Isi proses viewer: satu jendela riwayat atau per aplikasi, koneksi service sendiri, dan (riwayat) sampling Live sendiri.
/// Tidak membuat overlay/tray/hotkey/kuota dan tidak pernah menulis <c>settings.json</c>. Proses berakhir saat jendela
/// ditutup atau App utama (<see cref="ViewerArguments.ParentProcessId"/>) keluar.
/// </summary>
public sealed class ViewerSession : IDisposable
{
    private readonly Application _app;
    private readonly IAppLog _log;
    private readonly ServiceConnection _service;
    private AppHost? _host;
    private Process? _parent;

    private ViewerSession(Application app, IAppLog log)
    {
        _app = app;
        _log = log;
        _service = new ServiceConnection(new ServiceClientTransport(new ServiceClient()), new ReconnectBackoff());
        _service.SubscriberFailed += ex => log.Error("Service connection handler failed.", ex);
    }

    /// <summary>Nama file log per jenis viewer (rotasi log tidak aman lintas proses, jadi tidak berbagi <c>app.log</c>).</summary>
    public static string LogBaseName(ViewerKind kind) => kind == ViewerKind.History ? "viewer-history" : "viewer-perapp";

    public static ViewerSession Start(Application app, ViewerArguments arguments, IAppLog log, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(arguments);
        var session = new ViewerSession(app, log);
        try
        {
            session.Open(arguments, dataDirectory);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        return session;
    }

    private void Open(ViewerArguments arguments, string dataDirectory)
    {
        var session = this;
        var app = _app;
        var log = _log;
        session.WatchParent(arguments.ParentProcessId);
        session._service.Start();

        Window window;
        if (arguments.Kind == ViewerKind.History)
        {
            var store = new SettingsFileStore(Path.Combine(dataDirectory, "settings.json"), TimeProvider.System);
            session._host = new AppHost(log, store, ReadSettings(dataDirectory, log), canSaveSettings: false);
            var live = new LiveHistoryBuffer();
            foreach (var (up, down) in arguments.Live)
            {
                live.Add(up, down);
            }

            // Sampel pertama sampler baru belum punya delta (null): dilewati agar tidak ada lembah 0 palsu di grafik.
            session._host.ReadingUpdated += reading =>
            {
                if (reading is not null)
                {
                    live.Add(reading.UpBytesPerSecond, reading.DownBytesPerSecond);
                }
            };
            session._host.Start();
            window = new HistoryWindow(session._host, session._service, live);
        }
        else
        {
            window = new PerAppWindow(session._service);
        }

        window.Closed += (_, _) => app.Shutdown();
        window.Show();
        window.Activate();
        log.Info($"Viewer opened: {arguments.Kind}.");
    }

    public void Dispose()
    {
        _host?.Dispose();
        _service.Dispose();
        _parent?.Dispose();
    }

    // Hanya membaca: file rusak dibiarkan untuk ditangani App utama (rename .corrupt), viewer memakai default.
    private static UserSettings ReadSettings(string dataDirectory, IAppLog log)
    {
        var path = Path.Combine(dataDirectory, "settings.json");
        try
        {
            return File.Exists(path) ? SettingsSerializer.Deserialize(File.ReadAllText(path)) : new UserSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SettingsFormatException)
        {
            log.Warn("Viewer could not read settings; using defaults.");
            return new UserSettings();
        }
    }

    // Event Process.Exited datang dari thread pool.
    private void ShutdownLater() => _app.Dispatcher.BeginInvoke(() => _app.Shutdown());

    private void WatchParent(int parentId)
    {
        try
        {
            var parent = Process.GetProcessById(parentId);
            parent.EnableRaisingEvents = true;
            parent.Exited += (_, _) => ShutdownLater();
            _parent = parent;
            if (parent.HasExited)
            {
                ShutdownLater();
            }
        }
        catch (ArgumentException)
        {
            // Induk sudah keluar sebelum viewer sempat start.
            ShutdownLater();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Tanpa pemantauan induk viewer tetap keluar saat jendelanya ditutup.
            _log.Error("Viewer could not watch the main app process.", ex);
        }
    }
}
