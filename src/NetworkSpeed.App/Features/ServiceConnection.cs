using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Ipc;

namespace NetworkSpeed.App.Features;

/// <summary>Pengirim request ke service (produksi: <see cref="ServiceClientTransport"/>; test: palsu).</summary>
public interface IServiceTransport
{
    Task<PipeResponse> SendAsync(PipeRequest request, CancellationToken cancellationToken);
}

/// <summary>Transport produksi atas <see cref="ServiceClient"/> (server wajib sesi 0).</summary>
public sealed class ServiceClientTransport(ServiceClient client) : IServiceTransport
{
    public Task<PipeResponse> SendAsync(PipeRequest request, CancellationToken cancellationToken) => client.SendAsync(request, cancellationToken);
}

/// <summary>
/// Satu pintu App ke service (spec S-06): status ketersediaan, daftar adapter service, dan query. Tidak pernah
/// melempar exception atau memblokir pemanggil; kegagalan apa pun = "tidak tersedia", lalu reconnect dengan backoff.
/// <see cref="StateChanged"/> dipicu dari thread pool; pemanggil UI wajib marshal ke dispatcher.
/// </summary>
public sealed class ServiceConnection(IServiceTransport transport, ReconnectBackoff backoff) : IDisposable
{
    /// <summary>Jeda cek ulang saat service sehat.</summary>
    public static readonly TimeSpan HealthyPollInterval = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly TaskCompletionSource _firstCheck = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IReadOnlyList<AdapterInfo> _adapters = [];
    private bool _isAvailable;
    private bool _isPerAppAvailable;

    public event Action? StateChanged;

    /// <summary>Exception dari subscriber <see cref="StateChanged"/> atau loop, untuk dicatat ke log oleh App.</summary>
    public event Action<Exception>? SubscriberFailed;

    public bool IsAvailable => Volatile.Read(ref _isAvailable);

    public bool IsPerAppAvailable => Volatile.Read(ref _isPerAppAvailable);

    public IReadOnlyList<AdapterInfo> Adapters => Volatile.Read(ref _adapters);

    /// <summary>
    /// Selesai setelah cek status pertama, baik service tersedia maupun tidak. Jendela menunggu ini agar tidak sempat
    /// menampilkan "not available" sebelum status diketahui (proses viewer baru saja start).
    /// </summary>
    public Task FirstCheck => _firstCheck.Task;

    /// <summary>Memulai loop cek status di background.</summary>
    public void Start() => _ = Task.Run(() => LoopAsync(_stop.Token));

    /// <returns>Jeda sampai cek berikutnya (30 detik bila sehat, backoff bila gagal).</returns>
    public async Task<TimeSpan> RefreshAsync(CancellationToken cancellationToken)
    {
        var status = await SafeSendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus), cancellationToken).ConfigureAwait(false);
        var adapters = status.Ok
            ? await SafeSendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetAdapters), cancellationToken).ConfigureAwait(false)
            : status;

        if (status.Ok && adapters.Ok)
        {
            backoff.Reset();
            Volatile.Write(ref _adapters, adapters.Adapters ?? []);
            Update(available: true, perApp: status.Status?.EtwAvailable == true);
            _firstCheck.TrySetResult();
            return HealthyPollInterval;
        }

        Volatile.Write(ref _adapters, []);
        Update(available: false, perApp: false);
        _firstCheck.TrySetResult();
        return backoff.NextDelay();
    }

    /// <summary>Meneruskan query; jawaban "unavailable" menandai service tidak tersedia dan memicu cek ulang segera.</summary>
    public async Task<PipeResponse> QueryAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        var response = await SafeSendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.ErrorCode == PipeErrors.Unavailable && request.Operation != PipeOperations.GetPerApp)
        {
            Update(available: false, perApp: false);
            Wake();
        }

        return response;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _wake.Dispose();
    }

    private async Task<PipeResponse> SafeSendAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            return PipeResponse.Error(PipeErrors.Unavailable);
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan delay;
                try
                {
                    delay = await RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not ObjectDisposedException and not OutOfMemoryException)
                {
                    // Satu iterasi gagal tidak boleh mematikan loop; coba lagi dengan backoff.
                    SubscriberFailed?.Invoke(ex);
                    delay = backoff.NextDelay();
                }

                await _wake.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // App ditutup.
        }
        catch (ObjectDisposedException)
        {
            // App ditutup.
        }
    }

    private void Wake()
    {
        try
        {
            if (_wake.CurrentCount == 0)
            {
                _wake.Release();
            }
        }
        catch (Exception ex) when (ex is SemaphoreFullException or ObjectDisposedException)
        {
            // Sudah dibangunkan atau sedang ditutup.
        }
    }

    private void Update(bool available, bool perApp)
    {
        var changed = available != IsAvailable || perApp != IsPerAppAvailable;
        Volatile.Write(ref _isAvailable, available);
        Volatile.Write(ref _isPerAppAvailable, perApp);
        if (!changed)
        {
            return;
        }

        try
        {
            StateChanged?.Invoke();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Bug di subscriber tidak boleh menghentikan loop koneksi (status "tersedia/tidak" akan beku selamanya).
            SubscriberFailed?.Invoke(ex);
        }
    }
}
