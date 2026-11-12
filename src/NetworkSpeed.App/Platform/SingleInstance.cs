using System.Diagnostics;

namespace NetworkSpeed.App.Platform;

/// <summary>
/// Satu instance App per sesi user (spec §6, E-13). Instance kedua memberi sinyal ke instance pertama
/// untuk membuka Settings, lalu keluar. Objek bernama berada di namespace <c>Local\</c> (per sesi).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly RegisteredWaitHandle _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle signal, Action onSignal)
    {
        _mutex = mutex;
        _signal = signal;
        _registration = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => onSignal(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private static string Suffix { get; } = Process.GetCurrentProcess().SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string MutexName => @"Local\NetworkSpeed.App." + Suffix;

    private static string SignalName => @"Local\NetworkSpeed.App.ShowSettings." + Suffix;

    /// <returns><c>null</c> bila instance lain sudah berjalan di sesi ini.</returns>
    public static SingleInstance? TryAcquire(Action onShowSettingsRequested)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        return new SingleInstance(mutex, signal, onShowSettingsRequested);
    }

    public static void SignalExisting()
    {
        if (EventWaitHandle.TryOpenExisting(SignalName, out var signal))
        {
            using (signal)
            {
                signal.Set();
            }
        }
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _signal.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
