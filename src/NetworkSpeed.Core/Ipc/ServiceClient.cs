using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NetworkSpeed.Core.Ipc;

/// <summary>
/// Klien pipe untuk App (spec S-06): service tidak ada, timeout, atau jawaban rusak menghasilkan
/// <see cref="PipeErrors.Unavailable"/>, bukan exception. Backoff reconnect dilakukan pemanggil.
/// </summary>
/// <param name="requireSessionZeroServer">
/// <c>true</c> (produksi): server harus berjalan di sesi 0 (Windows Service). Mencegah proses user biasa yang lebih dulu
/// membuat pipe dengan nama sama (squatting) menyuplai data palsu ke App.
/// </param>
public sealed class ServiceClient(string pipeName = PipeProtocol.PipeName, TimeSpan? timeout = null, bool requireSessionZeroServer = true)
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(3);

    public async Task<PipeResponse> SendAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (requireSessionZeroServer && !IsServerInSessionZero(pipe.SafePipeHandle))
            {
                return PipeResponse.Error(PipeErrors.Unavailable);
            }

            await PipeProtocol.WriteAsync(pipe, PipeProtocol.Serialize(request), timeout.Token).ConfigureAwait(false);
            var payload = await PipeProtocol.ReadAsync(pipe, PipeProtocol.MaxResponseBytes, timeout.Token).ConfigureAwait(false);
            return PipeProtocol.DeserializeResponse(payload) ?? PipeResponse.Error(PipeErrors.Unavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PipeResponse.Error(PipeErrors.Unavailable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or PipeProtocolException)
        {
            return PipeResponse.Error(PipeErrors.Unavailable);
        }
    }

    private static bool IsServerInSessionZero(SafePipeHandle pipe) =>
        OperatingSystem.IsWindows()
        && GetNamedPipeServerSessionId(pipe, out var sessionId)
        && sessionId == 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out uint sessionId);
}
