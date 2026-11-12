using System.IO;

namespace NetworkSpeed.App.Features;

/// <summary>
/// Menunggu sinyal keluar dari installer (<see cref="Core.Lifecycle.AppExitSignal"/>, spec §8). Event dibuat service, jadi
/// bisa belum ada saat App start: <see cref="TryAttach"/> dipanggil lagi setiap status service berubah. Gagal membuka →
/// App berjalan normal (installer memakai taskkill sebagai fallback). <c>onExit</c> dipanggil sekali, dari thread pool.
/// </summary>
public sealed class AppExitListener(Func<WaitHandle?> open, Action onExit) : IDisposable
{
    private readonly Lock _gate = new();
    private WaitHandle? _handle;
    private RegisteredWaitHandle? _registration;
    private bool _fired;
    private bool _disposed;

    /// <returns><c>true</c> bila sudah/berhasil menunggu event.</returns>
    public bool TryAttach()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (_handle is not null)
            {
                return true;
            }

            WaitHandle? handle;
            try
            {
                handle = open();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
            {
                return false;
            }

            if (handle is null)
            {
                return false;
            }

            _handle = handle;
            _registration = ThreadPool.RegisterWaitForSingleObject(handle, (_, _) => Fire(), null, Timeout.Infinite, executeOnlyOnce: true);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _registration?.Unregister(null);
            _handle?.Dispose();
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed || _fired)
            {
                return;
            }

            _fired = true;
        }

        onExit();
    }
}
