using Microsoft.Extensions.Hosting;
using Microsoft.Win32.SafeHandles;
using NetworkSpeed.Core.Lifecycle;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Interop.Lifecycle;

namespace NetworkSpeed.Service.Workers;

/// <summary>
/// Membuat dan memegang event <see cref="AppExitSignal.Name"/> selama service hidup, agar installer bisa menutup App di
/// semua sesi (spec §8). Gagal membuat tidak menghentikan service: installer memakai taskkill sebagai fallback.
/// </summary>
public sealed class AppExitSignalHost(IAppLog log) : IHostedService, IDisposable
{
    private SafeWaitHandle? _event;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _event = GlobalEventNative.Create(AppExitSignal.Name, AppExitSignal.Sddl);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            log.Warn($"App exit signal could not be created (0x{ex.NativeErrorCode:X8}).");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _event?.Dispose();
}
